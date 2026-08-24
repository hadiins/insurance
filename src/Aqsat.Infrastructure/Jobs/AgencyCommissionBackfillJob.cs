using Aqsat.Application.Commission;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Jobs;

/// <summary>
/// One-time (re-runnable) backfill for policies issued before AgencyCommissionEntry existed —
/// mirrors PolicyNumberBackfillJob's batching (500 rows, no table-wide lock, ChangeTracker.Clear
/// between batches). Only scheduled installment policies with a locked AgencyCommissionPercent are
/// eligible; unscheduled ones are skipped (they naturally get entries the next time they're
/// scheduled or fully paid). Safe to re-run: a policy that already has AgencyCommissionEntry rows
/// is never touched again.
/// </summary>
public sealed class AgencyCommissionBackfillJob(AppDbContext dbContext)
{
    private const int BatchSize = 500;

    public async Task RunAsync(CancellationToken ct = default)
    {
        int processedThisBatch;
        do
        {
            var batch = await dbContext.Policies
                .Where(p => p.AgencyCommissionPercent != null && p.InstallmentCount > 0)
                .Where(p => !dbContext.AgencyCommissionEntries.Any(e => e.PolicyId == p.Id))
                .OrderBy(p => p.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            foreach (var policy in batch)
            {
                await BackfillPolicyAsync(policy, ct);
            }

            if (batch.Count > 0)
            {
                await dbContext.SaveChangesAsync(ct);
            }

            dbContext.ChangeTracker.Clear();
            processedThisBatch = batch.Count;
        } while (processedThisBatch == BatchSize);
    }

    private async Task BackfillPolicyAsync(Policy policy, CancellationToken ct)
    {
        var installments = await dbContext.Installments.AsNoTracking()
            .Where(i => i.PolicyId == policy.Id)
            .OrderBy(i => i.SeqNo)
            .ToListAsync(ct);
        if (installments.Count == 0)
        {
            return;
        }

        var ratePercent = policy.AgencyCommissionPercent!.Value;
        var slices = CommissionGenerator.Generate(
            policy.NetPremium, ratePercent, policy.TotalReceivable, policy.DownPayment,
            installments.Select(i => (i.Id, i.Amount)).ToList());

        DateTimeOffset? downPaymentEligibleAt = null;
        if (policy.DownPayment > 0)
        {
            var downPaymentReceipt = await dbContext.Payments.AsNoTracking()
                .Where(p => p.InstallmentIdHint == policy.Id && p.Amount == policy.DownPayment)
                .OrderByDescending(p => p.PaidOn)
                .FirstOrDefaultAsync(ct);
            downPaymentEligibleAt = downPaymentReceipt is not null
                ? downPaymentReceipt.PaidOn.ToDateTime(TimeOnly.MinValue)
                : policy.IssueDate.ToDateTime(TimeOnly.MinValue);
        }

        var installmentsById = installments.ToDictionary(i => i.Id);
        foreach (var slice in slices)
        {
            CommissionStatus status;
            DateTimeOffset? eligibleAt;

            if (slice.InstallmentId is null)
            {
                status = CommissionStatus.Payable;
                eligibleAt = downPaymentEligibleAt;
            }
            else
            {
                var installment = installmentsById[slice.InstallmentId.Value];
                if (installment.Status == InstallmentStatus.Settled)
                {
                    status = CommissionStatus.Payable;
                    var lastPayment = await dbContext.PaymentAllocations.AsNoTracking()
                        .Where(a => a.InstallmentId == installment.Id)
                        .Include(a => a.Payment)
                        .OrderByDescending(a => a.Payment.PaidOn)
                        .Select(a => (DateOnly?)a.Payment.PaidOn)
                        .FirstOrDefaultAsync(ct);
                    eligibleAt = (lastPayment ?? installment.DueDate).ToDateTime(TimeOnly.MinValue);
                }
                else
                {
                    status = CommissionStatus.Pending;
                    eligibleAt = null;
                }
            }

            dbContext.AgencyCommissionEntries.Add(new AgencyCommissionEntry
            {
                AgencyId = policy.AgencyId,
                PolicyId = policy.Id,
                InstallmentId = slice.InstallmentId,
                IsFullPolicySlice = false,
                BasePortion = slice.BasePortion,
                RatePercent = ratePercent,
                Amount = slice.Amount,
                Status = status,
                EligibleAt = eligibleAt,
            });
        }
    }
}
