using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Application.Payments;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Payments;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
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
public sealed class PaymentsController(AppDbContext dbContext, ICurrentUserContext currentUser, PaymentReversalService reversalService) : ControllerBase
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
        // One SaveChanges per item — an unbounded array would hold a request thread (and its DB
        // connection) for as long as it takes to record thousands of payments.
        if (items.Count is 0 or > 200)
        {
            return ValidationProblem("هر درخواست دسته‌ای باید بین ۱ تا ۲۰۰ پرداخت داشته باشد.");
        }

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
        var found = await reversalService.ReverseAsync(id, currentUser.UserId, currentUser.DisplayName, ct);
        if (!found)
        {
            return NotFound();
        }

        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<(PaymentResultDto? Result, string? Error)> RecordPaymentAsync(RecordPaymentRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0)
        {
            return (null, "مبلغ پرداخت باید مثبت باشد.");
        }

        if (!PaymentDateValidator.IsValid(request.PaidOn))
        {
            return (null, PaymentDateValidator.ErrorMessage);
        }

        // B16 — Method is free text that reports group on; only the four labels the receipt form
        // pairs with a PaymentMethod choice are acceptable from a client.
        if (!WellKnownPaymentMethods.OperatorMethodLabels.Contains(request.Method))
        {
            return (null, "روش پرداخت نامعتبر است.");
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
        if (payment.MethodType == PaymentMethod.Cheque)
        {
            if (request.Cheque is null)
            {
                return (null, "برای پرداخت چکی، مشخصات چک الزامی است.");
            }

            var chequeError = ChequeDetailsValidator.Validate(request.Cheque)
                ?? await CashBoxExistsAsync(request.Cheque.CashBoxId, ct);
            if (chequeError is not null)
            {
                return (null, chequeError);
            }

            dbContext.PaymentCheques.Add(new PaymentCheque
            {
                AgencyId = hint.AgencyId,
                Payment = payment,
                PolicyId = hint.PolicyId,
                ChequeNumber = request.Cheque.ChequeNumber.Trim(),
                BankName = request.Cheque.BankName.Trim(),
                DueDate = request.Cheque.DueDate,
                PresenterName = request.Cheque.PresenterName.Trim(),
                CashBoxId = request.Cheque.CashBoxId,
                Status = CollateralStatus.Held,
            });
        }

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
        catch (DbUpdateException ex)
        {
            DetachPendingChanges(dbContext.ChangeTracker);

            // A concurrent duplicate submission raced the pre-check above — the unique index caught
            // it at the database. Treat it as success, per rule 24, not as a failure.
            var raced = await FindExistingPaymentResultAsync(request, ct);
            if (raced is not null)
            {
                return (raced, null);
            }

            // The generic database message is a dead end for the operator — name the two failure
            // shapes a user can actually act on (rule 15) instead of one blanket message.
            if (ex.InnerException is SqlException { Number: 547 })
            {
                return (null, "صندوق یا حساب بانکی انتخاب‌شده یافت نشد؛ مقادیر را بازبینی کنید.");
            }

            if (ex.InnerException is SqlException { Number: 8152 or 2628 })
            {
                return (null, "طول یکی از مقادیر ارسالی بیش از حد مجاز است.");
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

    /// <summary>CLAUDE.md rule 11 — a cheque's CashBoxId must point at a real cash box inside the
    /// caller's agency. RLS scopes the lookup; an invisible box reads as "not found", never as an
    /// FK violation the user can't act on.</summary>
    private async Task<string?> CashBoxExistsAsync(Guid cashBoxId, CancellationToken ct) =>
        await dbContext.CashBoxes.AsNoTracking().AnyAsync(c => c.Id == cashBoxId, ct)
            ? null
            : "صندوق انتخاب‌شده یافت نشد.";

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
