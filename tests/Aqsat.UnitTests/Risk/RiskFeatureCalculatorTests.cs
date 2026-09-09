using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Risk;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Risk;

/// <summary>
/// The risk review's R3, verified end-to-end: the on-time ratio reads each settled installment's
/// LAST allocated payment. A reversed payment must never be that "last payment" — the reversal
/// soft-deletes the payment AND its allocations together (PaymentReversalService), so the
/// calculator's root-level soft-delete filter on PaymentAllocations already excludes it. This
/// test pins that pairing: if a reversal ever stopped deleting allocations, the old on-time
/// payment would mask the late replacement and the ratio would lie.
/// </summary>
public class RiskFeatureCalculatorTests
{
    [Fact]
    public async Task A_reversed_payment_is_not_the_settled_installments_last_payment()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;
        await InsuranceLineSeeder.EnsureSeededAsync(context);
        var lineId = await context.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var customer = new Customer { AgencyId = agencyA.AgencyId, ExternalCode = $"EXT-{Guid.NewGuid():N}"[..12], FullName = "مشتری ریسک" };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var policy = new Policy
        {
            AgencyId = agencyA.AgencyId,
            PolicyNumber = $"POL-RISK-{Guid.NewGuid():N}"[..18],
            InsuranceLineId = lineId,
            CustomerId = customer.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = today,
            StartDate = today,
            EndDate = today.AddYears(1),
            NetPremium = 1_000_000m,
            DownPayment = 0,
            InstallmentCount = 1,
        };
        context.Policies.Add(policy);
        await context.SaveChangesAsync();

        var installment = new Installment
        {
            AgencyId = agencyA.AgencyId,
            PolicyId = policy.Id,
            SeqNo = 1,
            DueDate = today,
            SettlementDeadline = today.AddDays(3),
            Amount = 1_000_000m,
            PaidAmount = 1_000_000m,
            Status = InstallmentStatus.Settled,
        };
        context.Installments.Add(installment);
        await context.SaveChangesAsync();

        // First attempt: paid on the due date, then reversed — payment and allocation soft-deleted
        // together, exactly as PaymentReversalService does it.
        var reversedPayment = new Payment
        {
            AgencyId = agencyA.AgencyId,
            CustomerId = customer.Id,
            InstallmentIdHint = installment.Id,
            Amount = 1_000_000m,
            PaidOn = today,
            Method = "نقدی",
        };
        context.Payments.Add(reversedPayment);
        await context.SaveChangesAsync();
        var reversedAllocation = new PaymentAllocation
        {
            AgencyId = agencyA.AgencyId,
            PaymentId = reversedPayment.Id,
            InstallmentId = installment.Id,
            Amount = 1_000_000m,
        };
        context.PaymentAllocations.Add(reversedAllocation);
        await context.SaveChangesAsync();

        var reversedAt = DateTimeOffset.UtcNow;
        reversedPayment.IsDeleted = true;
        reversedPayment.DeletedAt = reversedAt;
        reversedAllocation.IsDeleted = true;
        reversedAllocation.DeletedAt = reversedAt;

        // The replacement: settled ten days after the due date — late, full stop.
        var livePayment = new Payment
        {
            AgencyId = agencyA.AgencyId,
            CustomerId = customer.Id,
            InstallmentIdHint = installment.Id,
            Amount = 1_000_000m,
            PaidOn = today.AddDays(10),
            Method = "نقدی",
        };
        context.Payments.Add(livePayment);
        await context.SaveChangesAsync();
        context.PaymentAllocations.Add(new PaymentAllocation
        {
            AgencyId = agencyA.AgencyId,
            PaymentId = livePayment.Id,
            InstallmentId = installment.Id,
            Amount = 1_000_000m,
        });
        await context.SaveChangesAsync();

        var features = await new RiskFeatureCalculator(context).CalculateAsync(customer.Id, writeOffDays: 180);

        Assert.Equal(1, features.SettledInstallmentCount);
        Assert.Equal(0, features.OnTimeSettledCount);
        Assert.Equal(0m, features.OnTimeRatePercent);
    }
}
