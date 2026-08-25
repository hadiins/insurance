using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Application.Payments;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASKS.md Task 10 — payment recording + allocation per docs/PHASE-1-SPEC.md §3.4. A payment
/// is its own entity (CLAUDE.md rule 20); allocation defaults to oldest-due-date-first across the
/// customer's unsettled installments and can be overridden by the agent. Idempotency (rule 24) is
/// enforced twice: a pre-check for the common sequential case, and a unique-index catch for the
/// concurrent race — same defence-in-depth pattern Task 6's ImportService uses for duplicate rows.
/// </summary>
[ApiController]
[Route("api/payments")]
[Authorize(Policy = Permissions.PaymentWrite)]
public sealed class PaymentsController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PaymentResultDto>> Record(RecordPaymentRequest request, CancellationToken ct)
    {
        var (result, error) = await RecordPaymentAsync(request, ct);
        return error is not null ? ValidationProblem(error) : Ok(result);
    }

    [HttpPost("batch")]
    public async Task<ActionResult<IReadOnlyList<BatchPaymentResultItem>>> Batch(
        IReadOnlyList<RecordPaymentRequest> items, CancellationToken ct)
    {
        var results = new List<BatchPaymentResultItem>();
        foreach (var item in items)
        {
            var (result, error) = await RecordPaymentAsync(item, ct);
            results.Add(new BatchPaymentResultItem(item.InstallmentIdHint, result, error));
        }

        return Ok(results);
    }

    [HttpPost("{id:guid}/reverse")]
    public async Task<ActionResult> Reverse(Guid id, CancellationToken ct)
    {
        var payment = await dbContext.Payments
            .Include(p => p.Allocations).ThenInclude(a => a.Installment)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is null)
        {
            return NotFound();
        }

        var occurredAt = DateTimeOffset.UtcNow;
        foreach (var allocation in payment.Allocations)
        {
            var installment = allocation.Installment;
            var wasSettled = installment.Status == InstallmentStatus.Settled;
            installment.PaidAmount -= allocation.Amount;
            installment.Status = RecomputeStatus(installment.PaidAmount, installment.Amount);
            allocation.IsDeleted = true;
            allocation.DeletedAt = occurredAt;

            // docs/TASKS.md Task 13 — undoing the settlement that made a commission slice payable
            // must undo the activation too. This is not the "no clawback" case (rule: a customer
            // who simply stops paying never triggers this) — it is unwinding our own mistaken entry.
            if (wasSettled && installment.Status != InstallmentStatus.Settled)
            {
                var commissionEntry = await dbContext.CommissionEntries
                    .FirstOrDefaultAsync(c => c.InstallmentId == installment.Id, ct);
                if (commissionEntry is { Status: CommissionStatus.Payable })
                {
                    commissionEntry.Status = CommissionStatus.Pending;
                    commissionEntry.EligibleAt = null;
                }

                var agencyCommissionEntry = await dbContext.AgencyCommissionEntries
                    .FirstOrDefaultAsync(c => c.InstallmentId == installment.Id, ct);
                if (agencyCommissionEntry is { Status: CommissionStatus.Payable })
                {
                    agencyCommissionEntry.Status = CommissionStatus.Pending;
                    agencyCommissionEntry.EligibleAt = null;
                }
            }

            dbContext.AuditEntries.Add(new AuditEntry
            {
                AgencyId = installment.AgencyId,
                UserId = currentUser.UserId,
                UserDisplayName = currentUser.DisplayName,
                EntityType = nameof(Payment),
                EntityId = payment.Id,
                PolicyId = installment.PolicyId,
                Action = AuditAction.PaymentReversed,
                Description = $"برگشت پرداخت قسط شمارهٔ {installment.SeqNo}",
                OccurredAt = occurredAt,
                IpAddress = CurrentRequestContext.IpAddress,
            });
        }

        // A down-payment or non-installment full-payment receipt has no PaymentAllocation rows —
        // InstallmentIdHint holds the policy's own Id in both cases (no real FK, PaymentConfiguration.cs).
        // Reversing it must undo whichever commission slice its receipt made payable.
        if (payment.Allocations.Count == 0)
        {
            var policy = await dbContext.Policies.FirstOrDefaultAsync(p => p.Id == payment.InstallmentIdHint, ct);
            if (policy is not null)
            {
                if (policy.IsInstallment)
                {
                    var downCommissionEntry = await dbContext.CommissionEntries
                        .FirstOrDefaultAsync(c => c.PolicyId == policy.Id && c.InstallmentId == null, ct);
                    if (downCommissionEntry is { Status: CommissionStatus.Payable })
                    {
                        downCommissionEntry.Status = CommissionStatus.Pending;
                        downCommissionEntry.EligibleAt = null;
                    }

                    var downAgencyEntry = await dbContext.AgencyCommissionEntries
                        .FirstOrDefaultAsync(c => c.PolicyId == policy.Id && c.InstallmentId == null && !c.IsFullPolicySlice, ct);
                    if (downAgencyEntry is { Status: CommissionStatus.Payable })
                    {
                        downAgencyEntry.Status = CommissionStatus.Pending;
                        downAgencyEntry.EligibleAt = null;
                    }
                }
                else
                {
                    var fullPolicyEntry = await dbContext.AgencyCommissionEntries
                        .FirstOrDefaultAsync(c => c.PolicyId == policy.Id && c.IsFullPolicySlice, ct);
                    if (fullPolicyEntry is { Status: CommissionStatus.Payable })
                    {
                        fullPolicyEntry.Status = CommissionStatus.Pending;
                        fullPolicyEntry.EligibleAt = null;
                    }
                }

                dbContext.AuditEntries.Add(new AuditEntry
                {
                    AgencyId = policy.AgencyId,
                    UserId = currentUser.UserId,
                    UserDisplayName = currentUser.DisplayName,
                    EntityType = nameof(Payment),
                    EntityId = payment.Id,
                    PolicyId = policy.Id,
                    Action = AuditAction.PaymentReversed,
                    Description = policy.IsInstallment
                        ? $"برگشت پیش‌پرداخت بیمه‌نامهٔ {policy.PolicyNumber}"
                        : $"برگشت پرداخت کامل بیمه‌نامهٔ {policy.PolicyNumber}",
                    OccurredAt = occurredAt,
                    IpAddress = CurrentRequestContext.IpAddress,
                });
            }
        }

        payment.IsDeleted = true;
        payment.DeletedAt = occurredAt;

        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<(PaymentResultDto? Result, string? Error)> RecordPaymentAsync(RecordPaymentRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0)
        {
            return (null, "مبلغ پرداخت باید مثبت باشد.");
        }

        // Idempotency pre-check (CLAUDE.md rule 24) — the common sequential-duplicate case.
        var existing = await FindExistingPaymentResultAsync(request, ct);
        if (existing is not null)
        {
            return (existing, null);
        }

        var hint = await dbContext.Installments
            .Include(i => i.Policy)
            .FirstOrDefaultAsync(i => i.Id == request.InstallmentIdHint, ct);
        if (hint is null)
        {
            return (null, "قسط یافت نشد.");
        }

        var customerId = hint.Policy.CustomerId;

        // "unsettled installments of this customer" (§3.4) — a customer's installments can span
        // more than one policy, so this is not scoped to hint.PolicyId.
        var unsettled = await dbContext.Installments
            .Include(i => i.Policy)
            .Where(i => i.Policy.CustomerId == customerId && i.Status != InstallmentStatus.Settled)
            .OrderBy(i => i.DueDate)
            .ToListAsync(ct);

        List<(Guid InstallmentId, decimal Amount)> allocationPlan;
        decimal unallocated;

        if (request.Allocations is { Count: > 0 })
        {
            var byId = unsettled.ToDictionary(i => i.Id);
            var validationError = ValidateExplicitAllocations(request, byId);
            if (validationError is not null)
            {
                return (null, validationError);
            }

            allocationPlan = request.Allocations.Select(a => (a.InstallmentId, a.Amount)).ToList();
            unallocated = request.Amount - allocationPlan.Sum(a => a.Amount);
        }
        else
        {
            var balances = unsettled.Select(i => new InstallmentBalance(i.Id, i.Balance)).ToList();
            var plan = PaymentAllocator.Allocate(request.Amount, balances);
            allocationPlan = plan.Allocations.ToList();
            unallocated = plan.UnallocatedAmount;
        }

        var installmentsById = unsettled.ToDictionary(i => i.Id);

        var payment = new Payment
        {
            AgencyId = hint.AgencyId,
            CustomerId = customerId,
            InstallmentIdHint = request.InstallmentIdHint,
            Amount = request.Amount,
            PaidOn = request.PaidOn,
            Method = request.Method,
            ReferenceNo = request.ReferenceNo,
            MethodType = request.MethodType ?? PaymentMethod.Cash,
            CashBoxId = request.CashBoxId,
            BankAccountId = request.BankAccountId,
            RecordedByUserId = currentUser.UserId,
        };
        dbContext.Payments.Add(payment);

        var occurredAt = DateTimeOffset.UtcNow;
        var lines = new List<AllocationLineDto>();
        foreach (var (installmentId, amount) in allocationPlan)
        {
            var installment = installmentsById[installmentId];
            installment.PaidAmount += amount;
            installment.Status = RecomputeStatus(installment.PaidAmount, installment.Amount);

            // docs/TASKS.md Task 13 — full settlement flips this installment's commission slice to
            // payable (docs/PHASE-1-SPEC.md §3.4). `unsettled` was queried as Status != Settled, so
            // reaching Settled here is always a fresh transition, never a re-trigger.
            if (installment.Status == InstallmentStatus.Settled)
            {
                var commissionEntry = await dbContext.CommissionEntries
                    .FirstOrDefaultAsync(c => c.InstallmentId == installmentId, ct);
                if (commissionEntry is { Status: CommissionStatus.Pending })
                {
                    commissionEntry.Status = CommissionStatus.Payable;
                    commissionEntry.EligibleAt = occurredAt;
                }

                var agencyCommissionEntry = await dbContext.AgencyCommissionEntries
                    .FirstOrDefaultAsync(c => c.InstallmentId == installmentId, ct);
                if (agencyCommissionEntry is { Status: CommissionStatus.Pending })
                {
                    agencyCommissionEntry.Status = CommissionStatus.Payable;
                    agencyCommissionEntry.EligibleAt = occurredAt;
                }
            }

            dbContext.PaymentAllocations.Add(new PaymentAllocation
            {
                AgencyId = hint.AgencyId,
                Payment = payment,
                InstallmentId = installmentId,
                Amount = amount,
            });

            dbContext.AuditEntries.Add(new AuditEntry
            {
                AgencyId = installment.AgencyId,
                UserId = currentUser.UserId,
                UserDisplayName = currentUser.DisplayName,
                EntityType = nameof(Payment),
                EntityId = payment.Id,
                PolicyId = installment.PolicyId,
                Action = AuditAction.PaymentRecorded,
                Description = $"ثبت پرداخت قسط شمارهٔ {installment.SeqNo}",
                OccurredAt = occurredAt,
                IpAddress = CurrentRequestContext.IpAddress,
            });

            lines.Add(new AllocationLineDto(installmentId, installment.SeqNo, installment.Policy.PolicyNumber, amount));
        }

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            DetachPendingChanges(dbContext.ChangeTracker);

            // A concurrent duplicate submission raced the pre-check above — the unique index caught
            // it at the database. Treat it as success, per rule 24, not as a failure.
            var raced = await FindExistingPaymentResultAsync(request, ct);
            if (raced is not null)
            {
                return (raced, null);
            }

            return (null, "خطای پایگاه‌داده هنگام ثبت پرداخت.");
        }

        return (new PaymentResultDto(payment.Id, payment.Amount, lines, unallocated), null);
    }

    private async Task<PaymentResultDto?> FindExistingPaymentResultAsync(RecordPaymentRequest request, CancellationToken ct)
    {
        var existing = await dbContext.Payments
            .Include(p => p.Allocations).ThenInclude(a => a.Installment).ThenInclude(i => i.Policy)
            .FirstOrDefaultAsync(
                p => p.InstallmentIdHint == request.InstallmentIdHint && p.PaidOn == request.PaidOn && p.Amount == request.Amount,
                ct);
        if (existing is null)
        {
            return null;
        }

        var lines = existing.Allocations
            .Where(a => !a.IsDeleted)
            .Select(a => new AllocationLineDto(a.InstallmentId, a.Installment.SeqNo, a.Installment.Policy.PolicyNumber, a.Amount))
            .ToList();
        var unallocated = existing.Amount - lines.Sum(l => l.Amount);

        return new PaymentResultDto(existing.Id, existing.Amount, lines, unallocated);
    }

    private static string? ValidateExplicitAllocations(RecordPaymentRequest request, IReadOnlyDictionary<Guid, Installment> unsettledById)
    {
        decimal sum = 0;
        foreach (var line in request.Allocations!)
        {
            if (!unsettledById.TryGetValue(line.InstallmentId, out var installment))
            {
                return "قسط انتخاب‌شده برای تخصیص در دسترس نیست.";
            }

            if (line.Amount <= 0 || line.Amount > installment.Balance)
            {
                return "مبلغ تخصیص‌یافته به هر قسط نامعتبر است.";
            }

            sum += line.Amount;
        }

        if (sum > request.Amount)
        {
            return "مجموع تخصیص‌ها از مبلغ پرداخت بیشتر است.";
        }

        return null;
    }

    private static InstallmentStatus RecomputeStatus(decimal paidAmount, decimal amount) =>
        paidAmount <= 0 ? InstallmentStatus.Unpaid : paidAmount >= amount ? InstallmentStatus.Settled : InstallmentStatus.Partial;

    private static void DetachPendingChanges(ChangeTracker changeTracker)
    {
        foreach (var entry in changeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
