using Aqsat.Application.Sms;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Jobs;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Renewals;

/// <summary>
/// Task 16's own check (docs/TASKS.md): register a prospect expiring in 60 days with
/// NotifyDaysBefore = 2. Advance the clock to day 58 — exactly one reminder, to both the customer
/// and their marketer. Mark converted — linked to the new policy.
/// </summary>
public class RenewalWatchJobTests
{
    [Fact]
    public async Task Advancing_to_the_notify_offset_sends_exactly_one_reminder_each_to_customer_and_marketer_and_never_again()
    {
        await using var context = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(context);
        await InsuranceLineSeeder.EnsureSeededAsync(context);
        AgencyContext.Current = fixture.AgencyAId;

        var thirdPartyLineId = await context.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var today = new DateOnly(2026, 3, 10);

        var marketer = new Marketer { AgencyId = fixture.AgencyAId, FullName = "بازاریاب تمدید", Mobile = "09121110000", Type = MarketerType.Independent };
        context.Marketers.Add(marketer);
        await context.SaveChangesAsync();

        var watch = new RenewalWatch
        {
            AgencyId = fixture.AgencyAId,
            ProspectName = "مشتری احتمالی",
            ProspectMobile = "09129990000",
            InsuranceLineId = thirdPartyLineId,
            CurrentInsurer = "شرکت بیمهٔ دیگر",
            CurrentExpiryDate = today.AddDays(60),
            NotifyDaysBefore = 2,
            MarketerId = marketer.Id,
            Status = RenewalWatchStatus.Watching,
        };
        context.RenewalWatches.Add(watch);
        await context.SaveChangesAsync();

        var sender = new RecordingSmsSender();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var job = new RenewalWatchJob(context, sender, timeProvider);

        // Day 0..57: 60, 59, 58 days out is only reached after advancing 58 days — nothing fires yet.
        await job.RunAsync();
        Assert.Empty(sender.SentTo);

        // Day 58: exactly 2 days from expiry — matches NotifyDaysBefore.
        timeProvider.Advance(TimeSpan.FromDays(58));
        await job.RunAsync();
        Assert.Equal(2, sender.SentTo.Count);
        Assert.Contains("09129990000", sender.SentTo);
        Assert.Contains("09121110000", sender.SentTo);

        var reloaded = await context.RenewalWatches.AsNoTracking().FirstAsync(w => w.Id == watch.Id);
        Assert.Equal(RenewalWatchStatus.Notified, reloaded.Status);

        var logCount = await context.ReminderLogs.AsNoTracking().CountAsync(r => r.RenewalWatchId == watch.Id);
        Assert.Equal(2, logCount);

        // Running the same day again must not re-send — idempotency per (watch, offset, recipient).
        sender.SentTo.Clear();
        await job.RunAsync();
        Assert.Empty(sender.SentTo);

        // A day later the offset no longer matches — still nothing.
        timeProvider.Advance(TimeSpan.FromDays(1));
        await job.RunAsync();
        Assert.Empty(sender.SentTo);
    }

    [Fact]
    public async Task An_active_policy_approaching_its_own_end_date_gets_an_auto_created_watch_once()
    {
        await using var context = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(context);
        await InsuranceLineSeeder.EnsureSeededAsync(context);
        AgencyContext.Current = fixture.AgencyAId;

        var thirdPartyLineId = await context.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var today = new DateOnly(2026, 3, 10);
        var customer = new Customer { AgencyId = fixture.AgencyAId, ExternalCode = $"EXT-{Guid.NewGuid():N}"[..12], FullName = "مشتری تمدید خودکار", Mobile = "09123334444" };
        var vehicle = new Vehicle { AgencyId = fixture.AgencyAId, Plate = "77ج777" };
        context.Customers.Add(customer);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        var policy = new Policy
        {
            AgencyId = fixture.AgencyAId,
            PolicyNumber = $"POL-REN-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = thirdPartyLineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = false,
            IssueDate = today.AddYears(-1),
            StartDate = today.AddYears(-1),
            EndDate = today.AddDays(30),
            NetPremium = 5_000_000m,
            Status = PolicyStatus.Active,
        };
        context.Policies.Add(policy);
        await context.SaveChangesAsync();

        var sender = new RecordingSmsSender();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var job = new RenewalWatchJob(context, sender, timeProvider);

        await job.RunAsync();

        var watches = await context.RenewalWatches.AsNoTracking()
            .Where(w => w.CustomerId == customer.Id).ToListAsync();
        Assert.Single(watches);
        Assert.Equal(policy.EndDate, watches[0].CurrentExpiryDate);
        Assert.Equal(RenewalWatchStatus.Watching, watches[0].Status);

        // Running again the same day must not create a duplicate watch for the same policy.
        await job.RunAsync();
        var watchesAfterSecondRun = await context.RenewalWatches.AsNoTracking()
            .Where(w => w.CustomerId == customer.Id).ToListAsync();
        Assert.Single(watchesAfterSecondRun);
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
