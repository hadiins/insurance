using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Payments;

/// <summary>
/// The single reversal path for any Payment — extracted from PaymentsController.Reverse so a
/// cheque bounce (PaymentChequesController) can trigger exactly the same unwind instead of a
/// second, divergent copy: unallocate (installment payments), undo the down-payment/full-payment
/// commission-slice Payable flip (bare Payments), soft-delete the Payment. Caller is responsible
/// for SaveChangesAsync — this only mutates the tracked graph so multiple reversal-adjacent changes
/// (e.g. also flipping a PaymentCheque's own Status) land in one transaction.
/// </summary>
public sealed class PaymentReversalService(AppDbContext dbContext)
{
    public async Task<bool> ReverseAsync(Guid paymentId, Guid userId, string userDisplayName, CancellationToken ct)
    {
        var payment = await dbContext.Payments
            .Include(p => p.Allocations).ThenInclude(a => a.Installment)
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null)
        {
            return false;
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
                UserId = userId,
                UserDisplayName = userDisplayName,
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
                    UserId = userId,
                    UserDisplayName = userDisplayName,
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

        return true;
    }

    private static InstallmentStatus RecomputeStatus(decimal paidAmount, decimal amount) =>
        paidAmount <= 0 ? InstallmentStatus.Unpaid : paidAmount >= amount ? InstallmentStatus.Settled : InstallmentStatus.Partial;
}
