using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Risk;

/// <summary>
/// Reads the customer's live financial position once per assessment (docs Phase 2A §3): a handful
/// of set-based queries over the existing customers/installments/payments/cheques/collateral
/// tables — the risk feature never duplicates that data into its own model. All reads run inside
/// the caller's RLS scope.
/// </summary>
public sealed class RiskFeatureCalculator(AppDbContext dbContext)
{
    public async Task<RiskFeatures> CalculateAsync(Guid customerId, int writeOffDays, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var policies = await dbContext.Policies.AsNoTracking()
            .Where(p => p.CustomerId == customerId)
            .Select(p => new { p.Status, p.StartDate, p.EndDate, p.IsRenewal })
            .ToListAsync(ct);
        var policyIds = await dbContext.Policies.AsNoTracking()
            .Where(p => p.CustomerId == customerId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var installments = await dbContext.Installments.AsNoTracking()
            .Where(i => policyIds.Contains(i.PolicyId))
            .Select(i => new { i.Status, i.DueDate, i.Amount, i.PaidAmount })
            .ToListAsync(ct);

        var open = installments.Where(i => i.Status != InstallmentStatus.Settled).ToList();
        var settled = installments.Count(i => i.Status == InstallmentStatus.Settled);

        var currentDebt = open.Sum(i => i.Amount - i.PaidAmount);
        var overdue = open.Where(i => i.DueDate < today).ToList();
        var maxDaysOverdue = overdue.Count == 0 ? 0 : overdue.Max(i => today.DayNumber - i.DueDate.DayNumber);
        var hasDefaultHistory = overdue.Any(i => today.DayNumber - i.DueDate.DayNumber > writeOffDays);

        // On-time = a settled installment whose LAST allocated payment landed by its due date
        // (rule 21 — an installment stays open until fully settled, so the last payment is the
        // settling one). Down payments are not installments and never enter this ratio.
        var settledInstallmentIds = await dbContext.Installments.AsNoTracking()
            .Where(i => policyIds.Contains(i.PolicyId) && i.Status == InstallmentStatus.Settled)
            .Select(i => new { i.Id, i.DueDate })
            .ToListAsync(ct);
        var settledIds = settledInstallmentIds.Select(i => i.Id).ToList();

        var lastPaidOnByInstallment = await dbContext.PaymentAllocations.AsNoTracking()
            .Where(a => settledIds.Contains(a.InstallmentId))
            .GroupBy(a => a.InstallmentId)
            .Select(g => new { InstallmentId = g.Key, LastPaidOn = g.Max(a => a.Payment.PaidOn) })
            .ToDictionaryAsync(x => x.InstallmentId, x => x.LastPaidOn, ct);

        var onTimeSettled = settledInstallmentIds.Count(i =>
            lastPaidOnByInstallment.TryGetValue(i.Id, out var lastPaidOn) && lastPaidOn <= i.DueDate);

        // Returned cheques: both payment cheques that bounced and bounced guarantee cheques
        // (Collateral) — the same pair the existing high-risk list counts.
        var bouncedPaymentCheques = await dbContext.PaymentCheques.AsNoTracking()
            .Where(q => q.Status == CollateralStatus.Bounced && q.Payment.CustomerId == customerId)
            .CountAsync(ct);
        var bouncedCollateral = await dbContext.Collaterals.AsNoTracking()
            .Where(c => c.Status == CollateralStatus.Bounced && policyIds.Contains(c.PolicyId))
            .CountAsync(ct);

        var firstIssueDate = policies.Count == 0 ? (DateOnly?)null : policies.Min(p => p.StartDate);
        var tenureMonths = firstIssueDate is { } first
            ? Math.Max(0, (today.Year - first.Year) * 12 + today.Month - first.Month)
            : 0;

        return new RiskFeatures(
            PolicyCount: policies.Count,
            ActivePolicyCount: policies.Count(p => p.Status == PolicyStatus.Active && p.EndDate >= today),
            RenewalCount: policies.Count(p => p.IsRenewal),
            TenureMonths: tenureMonths,
            CurrentDebtToman: currentDebt,
            OverdueAmountToman: overdue.Sum(i => i.Amount - i.PaidAmount),
            OverdueCount: overdue.Count,
            MaxDaysOverdue: maxDaysOverdue,
            HasDefaultHistory: hasDefaultHistory,
            ReturnedChequeCount: bouncedPaymentCheques + bouncedCollateral,
            SettledInstallmentCount: settled,
            OnTimeSettledCount: onTimeSettled);
    }
}
