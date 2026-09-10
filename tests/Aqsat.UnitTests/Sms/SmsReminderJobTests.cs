using Aqsat.Application.Sms;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Jobs;
using Aqsat.Infrastructure.Payments;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Portal;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aqsat.UnitTests.Sms;

/// <summary>
/// Task 14's own check (docs/TASKS.md) — a cost test, not a feature test: seed 100 installments,
/// settle 85 before the second reminder window, run the job. Exactly 15 second reminders sent, not
/// 100. CLAUDE.md rule 25's whole point (~185,000 toman/month/customer) rides on this being right.
/// </summary>
public class SmsReminderJobTests
{
    private static SmsReminderJob CreateJob(
        AppDbContext context, ISmsSender sender, TimeProvider timeProvider) =>
        new(context, sender, timeProvider,
            new InstallmentPaymentLinkService(context, [new MockPaymentGateway(NullLogger<MockPaymentGateway>.Instance)]),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Portal:PublicBaseUrl"] = "https://portal.example.com",
            }).Build(),
            NullLogger<SmsReminderJob>.Instance);

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
        var job = CreateJob(context, sender, timeProvider);

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

    [Fact]
    public async Task With_the_portal_on_the_reminder_carries_a_payment_link_minted_on_demand()
    {
        await using var context = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(context);
        await InsuranceLineSeeder.EnsureSeededAsync(context);
        AgencyContext.Current = fixture.AgencyAId;
        context.OrgSettings.Add(new OrgSettings { OrganizationId = fixture.AgencyAId, CustomerPortalEnabled = true, PaymentLinkTtlDays = 30 });

        // A fixed "today" this test owns outright — the shared database keeps every other test's
        // seeded installments, so real-time dates would pick those up too.
        var today = new DateOnly(2026, 6, 15);
        var (customerId, installmentId, policyNumber) = await SeedOneDueInstallmentAsync(context, fixture.AgencyAId, today);

        var sender = new RecordingSmsSender();
        var job = CreateJob(context, sender, new MutableTimeProvider(AtMidnightUtc(today)));
        await job.RunAsync();

        // One SMS carrying the /pay/{token} link, and the link row behind it. Scoped to this
        // test's own policy: the reminder job is platform-wide, so a concurrently-running test's
        // due installment in the shared DB can legitimately add an SMS to the same sender.
        var text = Assert.Single(sender.SentTexts.Where(t => t.Contains(policyNumber)));
        var link = Assert.Single(await context.CustomerPaymentLinks.AsNoTracking()
            .Where(l => l.CustomerId == customerId && l.Status == PaymentLinkStatus.Active).ToListAsync());
        Assert.Contains($"/pay/{link.Token}", text);
        Assert.NotNull(link.LastSentAtUtc);
        Assert.True(link.ExpiresAtUtc > DateTimeOffset.UtcNow.AddDays(29));

        // The token→agency index row exists so the anonymous page can resolve the scope.
        Assert.True(await context.PaymentLinkTokenIndex.AsNoTracking().AnyAsync(t => t.Token == link.Token));

        // A second run at the "3" offset reuses the SAME link (one active link per customer) —
        // never a second token.
        sender.SentTexts.Clear();
        AgencyContext.Current = fixture.AgencyAId;
        var job2 = CreateJob(context, sender, new MutableTimeProvider(AtMidnightUtc(today.AddDays(4))));
        await job2.RunAsync();
        Assert.Contains($"/pay/{link.Token}", Assert.Single(sender.SentTexts.Where(t => t.Contains(policyNumber))));
        var linksAfter = await context.CustomerPaymentLinks.AsNoTracking()
            .Where(l => l.CustomerId == customerId && !l.IsDeleted).ToListAsync();
        Assert.Single(linksAfter);

        // And the reminder's installment was not disturbed by the link minting (rule 25 unchanged):
        // exactly one log per (installment, offset) across the two windows.
        Assert.Equal(1, await context.ReminderLogs.AsNoTracking().CountAsync(r => r.InstallmentId == installmentId && r.OffsetDays == 7));
        Assert.Equal(1, await context.ReminderLogs.AsNoTracking().CountAsync(r => r.InstallmentId == installmentId && r.OffsetDays == 3));
    }

    [Fact]
    public async Task With_the_portal_off_the_reminder_stays_plain_text_and_no_link_is_minted()
    {
        await using var context = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(context);
        await InsuranceLineSeeder.EnsureSeededAsync(context);
        AgencyContext.Current = fixture.AgencyAId;
        // No OrgSettings row at all — a fresh agency's defaults: portal off.

        var today = new DateOnly(2026, 7, 20);
        var (customerId, _, policyNumber) = await SeedOneDueInstallmentAsync(context, fixture.AgencyAId, today);

        var sender = new RecordingSmsSender();
        var job = CreateJob(context, sender, new MutableTimeProvider(AtMidnightUtc(today)));
        await job.RunAsync();

        var text = Assert.Single(sender.SentTexts.Where(t => t.Contains(policyNumber)));
        Assert.DoesNotContain("/pay/", text);
        Assert.Equal(0, await context.CustomerPaymentLinks.CountAsync(l => l.CustomerId == customerId));
    }

    [Fact]
    public void The_template_puts_the_link_in_its_place_for_default_and_custom_bodies()
    {
        var plain = InstallmentReminderTemplate.Render("74B321", 2, 1_500_000m, new DateOnly(2026, 3, 17));
        Assert.DoesNotContain("/pay/", plain);
        Assert.Contains("74B321", plain);

        var withLink = InstallmentReminderTemplate.Render("74B321", 2, 1_500_000m, new DateOnly(2026, 3, 17), null, "https://x.example/pay/TOK");
        Assert.EndsWith("پرداخت آنلاین: https://x.example/pay/TOK", withLink);

        // A custom body that asks for the placeholder gets it substituted in place, not appended.
        var custom = InstallmentReminderTemplate.Render(
            "74B321", 2, 1_500_000m, new DateOnly(2026, 3, 17), "قسط {SeqNo}: {Balance} — {PaymentLink}", "https://x.example/pay/TOK");
        Assert.Equal("قسط 2: 1,500,000 — https://x.example/pay/TOK", custom);
    }

    private static DateTimeOffset AtMidnightUtc(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static async Task<(Guid CustomerId, Guid InstallmentId, string PolicyNumber)> SeedOneDueInstallmentAsync(
        AppDbContext context, Guid agencyId, DateOnly today)
    {
        var thirdPartyLineId = context.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).First();
        var customer = new Customer { AgencyId = agencyId, ExternalCode = $"EXT-{Guid.NewGuid():N}"[..12], FullName = "مشتری لینک", Mobile = "09121234599" };
        var vehicle = new Vehicle { AgencyId = agencyId, Plate = "12ط345" };
        context.Customers.Add(customer);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        var policy = new Policy
        {
            AgencyId = agencyId,
            PolicyNumber = $"POL-LNK-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = thirdPartyLineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = today,
            StartDate = today,
            EndDate = today.AddYears(1),
            NetPremium = 3_000_000m,
            DownPayment = 0,
            InstallmentCount = 1,
        };
        context.Policies.Add(policy);
        await context.SaveChangesAsync();

        var dueDate = today.AddDays(7);
        var installment = new Installment
        {
            AgencyId = agencyId,
            PolicyId = policy.Id,
            SeqNo = 1,
            DueDate = dueDate,
            SettlementDeadline = dueDate.AddDays(3),
            Amount = 3_000_000m,
            Status = InstallmentStatus.Unpaid,
        };
        context.Installments.Add(installment);
        await context.SaveChangesAsync();
        return (customer.Id, installment.Id, policy.PolicyNumber);
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
        public List<string> SentTexts { get; } = [];

        public Task<bool> SendAsync(string mobile, string text, Guid agencyId, CancellationToken ct = default)
        {
            SentTo.Add(mobile);
            SentTexts.Add(text);
            return Task.FromResult(true);
        }
    }
}
