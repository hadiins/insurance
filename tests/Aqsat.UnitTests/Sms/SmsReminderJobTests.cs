using Aqsat.Application.Sms;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Jobs;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Sms;

/// <summary>
/// Task 14's own check (docs/TASKS.md) — a cost test, not a feature test: seed 100 installments,
/// settle 85 before the second reminder window, run the job. Exactly 15 second reminders sent, not
/// 100. CLAUDE.md rule 25's whole point (~185,000 toman/month/customer) rides on this being right.
/// </summary>
public class SmsReminderJobTests
{
    [Fact]
    public async Task Settling_most_installments_before_the_second_window_sends_only_the_remaining_ones_a_second_reminder()
    {
        await using var context = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(context);
        await InsuranceLineSeeder.EnsureSeededAsync(context);
        AgencyContext.Current = fixture.AgencyAId;

        var thirdPartyLineId = await context.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var today = new DateOnly(2026, 3, 10);
        var customer = new Customer { AgencyId = fixture.AgencyAId, ExternalCode = $"EXT-{Guid.NewGuid():N}"[..12], FullName = "مشتری یادآور", Mobile = "09121234567" };
        var vehicle = new Vehicle { AgencyId = fixture.AgencyAId, Plate = "88ح888" };
        context.Customers.Add(customer);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        var policy = new Policy
        {
            AgencyId = fixture.AgencyAId,
            PolicyNumber = $"POL-SMS-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = thirdPartyLineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = today,
            StartDate = today,
            EndDate = today.AddYears(1),
            NetPremium = 10_000_000m,
            DownPayment = 0,
            InstallmentCount = 100,
        };
        context.Policies.Add(policy);
        await context.SaveChangesAsync();

        // Due in exactly 7 days from "today" — matches the first offset in the default "7,3,0".
        var dueDate = today.AddDays(7);
        var installments = Enumerable.Range(1, 100)
            .Select(seq => new Installment
            {
                AgencyId = fixture.AgencyAId,
                PolicyId = policy.Id,
                SeqNo = seq,
                DueDate = dueDate,
                SettlementDeadline = dueDate.AddDays(3),
                Amount = 100_000m,
                Status = InstallmentStatus.Unpaid,
            })
            .ToList();
        context.Installments.AddRange(installments);
        await context.SaveChangesAsync();

        var sender = new RecordingSmsSender();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var job = new SmsReminderJob(context, sender, timeProvider);

        // Day 0: due date is 7 days out — the first ("7") offset fires for all 100.
        await job.RunAsync();
        Assert.Equal(100, sender.SentTo.Count);

        var firstWindowLogCount = await context.ReminderLogs.AsNoTracking().CountAsync(r => r.OffsetDays == 7);
        Assert.Equal(100, firstWindowLogCount);

        // Settle 85 of the 100 before the second ("3") window opens. Re-fetched, not reused from
        // the `installments` list above — the job clears its DbContext's change tracker after each
        // agency, so those original instances are now detached and a mutation on them would
        // silently never reach the database.
        var idsToSettle = installments.Take(85).Select(i => i.Id).ToHashSet();
        var toSettle = await context.Installments.Where(i => idsToSettle.Contains(i.Id)).ToListAsync();
        foreach (var installment in toSettle)
        {
            installment.PaidAmount = installment.Amount;
            installment.Status = InstallmentStatus.Settled;
        }
        await context.SaveChangesAsync();

        // Advance 4 days: due date is now 3 days out — the second offset.
        sender.SentTo.Clear();
        timeProvider.Advance(TimeSpan.FromDays(4));
        await job.RunAsync();

        Assert.Equal(15, sender.SentTo.Count);

        var secondWindowLogCount = await context.ReminderLogs.AsNoTracking().CountAsync(r => r.OffsetDays == 3);
        Assert.Equal(15, secondWindowLogCount);

        // Running the same day again must not re-send — idempotency per (installment, offset).
        sender.SentTo.Clear();
        await job.RunAsync();
        Assert.Empty(sender.SentTo);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class RecordingSmsSender : ISmsSender
    {
        public List<string> SentTo { get; } = [];

        public Task<bool> SendAsync(string mobile, string text, Guid agencyId, CancellationToken ct = default)
        {
            SentTo.Add(mobile);
            return Task.FromResult(true);
        }
    }
}
