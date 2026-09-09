using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Sms;

/// <summary>
/// The effectiveness report's attribution rule (feature #2): a reminder is effective when an
/// allocation for its installment lands within attributionDays of the send, any channel. These
/// tests lock the window's math — both bounds inclusive, before-reminder payments never count,
/// Failed never enters the denominator — plus per-offset / per-month groupings, the detail page's
/// DaysToPay, RLS isolation across agencies, and the Persian validation failures.
///
/// Every test queries its OWN one-month window: the test database is shared and never wiped
/// between tests, so a shared window would make exact counts depend on execution order.
/// </summary>
[Collection("WebApplicationFactory")]
public class SmsEffectivenessReportEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SmsEffectivenessReportEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private static async Task<DevSeeder.SeededAuthFixture> GetOrCreateFixtureAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        return await DevSeeder.SeedAuthFixtureAsync(seedContext);
    }

    /// <summary>Login client tied to THIS fixture — a separate GetOrCreateFixtureAsync call would
    /// mint a different manager mobile, and the header's org id would belong to another user.</summary>
    private async Task<HttpClient> LoginForAsync(DevSeeder.SeededAuthFixture fixture, Guid organizationId)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", organizationId.ToString());
        return client;
    }

    private static string Url(DateOnly from, DateOnly to, int attributionDays = 7) =>
        $"/api/sms/effectiveness-report?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&attributionDays={attributionDays}";

    private static string DetailsUrl(DateOnly from, DateOnly to, int attributionDays = 7) =>
        $"/api/sms/effectiveness-report/details?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&attributionDays={attributionDays}";

    /// <summary>Seeds a customer + single-installment policy, the given reminder sends, and one
    /// payment allocation. The send dates must land inside the test's own query window.</summary>
    private static async Task<Installment> SeedRemindedInstallmentAsync(
        AppDbContext context, Guid agencyId, string customerName,
        DateOnly? paidOn, decimal paidAmount,
        IReadOnlyList<(int OffsetDays, DateOnly SentDate)> reminderSends,
        ReminderSendStatus status = ReminderSendStatus.Sent,
        decimal installmentAmount = 2_000_000m,
        DateOnly? dueDate = null)
    {
        await InsuranceLineSeeder.EnsureSeededAsync(context);
        var thirdPartyLineId = await context.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var customer = new Customer
        {
            AgencyId = agencyId,
            ExternalCode = $"EXT-{Guid.NewGuid():N}"[..20],
            FullName = customerName,
            Mobile = $"09{Guid.NewGuid():N}"[..11],
            NationalId = $"00{Guid.NewGuid():N}"[..10],
        };
        var vehicle = new Vehicle { AgencyId = agencyId, Plate = $"55{Guid.NewGuid():N}"[..2] };
        context.Customers.Add(customer);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        var sendDate = reminderSends[0].SentDate;
        var policy = new Policy
        {
            AgencyId = agencyId,
            PolicyNumber = $"POL-EFF-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = thirdPartyLineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = sendDate,
            StartDate = sendDate,
            EndDate = sendDate.AddYears(1),
            NetPremium = installmentAmount,
            DownPayment = 0,
            InstallmentCount = 1,
        };
        context.Policies.Add(policy);
        await context.SaveChangesAsync();

        var installment = new Installment
        {
            // Client-generated so the allocation below can reference it in the same SaveChanges.
            Id = SequentialGuidGenerator.Next(),
            AgencyId = agencyId,
            PolicyId = policy.Id,
            SeqNo = 1,
            DueDate = dueDate ?? sendDate.AddDays(20),
            SettlementDeadline = (dueDate ?? sendDate.AddDays(20)).AddDays(3),
            Amount = installmentAmount,
            Status = InstallmentStatus.Unpaid,
        };
        context.Installments.Add(installment);

        foreach (var (offsetDays, sentDate) in reminderSends)
        {
            context.ReminderLogs.Add(new ReminderLog
            {
                AgencyId = agencyId,
                InstallmentId = installment.Id,
                RecipientType = ReminderRecipientType.Customer,
                Mobile = customer.Mobile!,
                OffsetDays = offsetDays,
                TemplateKey = "installment-reminder-v1",
                Channel = ReminderChannel.Sms,
                Status = status,
                SentAt = new DateTimeOffset(sentDate.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
            });
        }

        if (paidAmount > 0 && paidOn is { } on)
        {
            // Client-generated so the allocation can reference it in the same SaveChanges — EF
            // cannot fix up a scalar FK without a navigation (same reason as the installment above).
            var payment = new Payment
            {
                Id = SequentialGuidGenerator.Next(),
                AgencyId = agencyId,
                CustomerId = customer.Id,
                InstallmentIdHint = installment.Id,
                Amount = paidAmount,
                PaidOn = on,
                Method = "نقدی",
                MethodType = PaymentMethod.Cash,
                RecordedByUserId = Guid.NewGuid(),
            };
            context.Payments.Add(payment);
            context.PaymentAllocations.Add(new PaymentAllocation
            {
                AgencyId = agencyId,
                PaymentId = payment.Id,
                InstallmentId = installment.Id,
                Amount = paidAmount,
            });
        }

        await context.SaveChangesAsync();
        return installment;
    }

    [Fact]
    public async Task Summary_counts_only_in_window_payments_and_excludes_failed_from_the_denominator()
    {
        var fixture = await GetOrCreateFixtureAsync();
        var client = await LoginForAsync(fixture, fixture.AgencyAId);
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;

        // Window: 2027-01 (this test's alone). Sends on 2027-01-10.
        var sendDate = new DateOnly(2027, 1, 10);
        var tag = Guid.NewGuid().ToString("N")[..6];

        // In-window: PaidOn == SentDate + 7 — the window's last day, inclusive.
        await SeedRemindedInstallmentAsync(
            context, fixture.AgencyAId, $"{tag}-in", sendDate.AddDays(7), 1_500_000m, [(7, sendDate)]);
        // Out-of-window: PaidOn == SentDate + 8 — one day past N=7.
        await SeedRemindedInstallmentAsync(
            context, fixture.AgencyAId, $"{tag}-out", sendDate.AddDays(8), 1_000_000m, [(7, sendDate)]);
        // Payment BEFORE the reminder — the installment still enters the report (it was reminded),
        // but its payment never counts as effective.
        await SeedRemindedInstallmentAsync(
            context, fixture.AgencyAId, $"{tag}-before", sendDate.AddDays(-3), 1_000_000m, [(7, sendDate)]);
        // Failed send: excluded from the denominator.
        await SeedRemindedInstallmentAsync(
            context, fixture.AgencyAId, $"{tag}-fail", sendDate.AddDays(2), 1_000_000m,
            [(7, sendDate)], status: ReminderSendStatus.Failed);
        // Same-day payment (PaidOn == SentAt.Date) — the window's first day, inclusive.
        await SeedRemindedInstallmentAsync(
            context, fixture.AgencyAId, $"{tag}-same", sendDate, 500_000m, [(3, sendDate)]);

        var report = await client.GetFromJsonAsync<SmsEffectivenessReportDto>(
            Url(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31)));

        var summary = report!.Summary;
        Assert.Equal(4, summary.RemindersSent);   // in + out + before + same
        Assert.Equal(1, summary.RemindersFailed);
        Assert.Equal(4, summary.DistinctInstallments);
        Assert.Equal(2, summary.PaidWithinWindow);   // in + same
        Assert.Equal(50.0m, summary.EffectivenessRate);
        Assert.Equal(2_000_000m, summary.AttributedCollectedToman);   // 1,500,000 + 500,000
        Assert.Equal(460m, summary.EstimatedCostToman);   // 4 × 115 toman
    }

    [Fact]
    public async Task Offset_rows_answer_independently()
    {
        var fixture = await GetOrCreateFixtureAsync();
        var client = await LoginForAsync(fixture, fixture.AgencyAId);
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;

        var tag = Guid.NewGuid().ToString("N")[..6];
        // Two sends at different offsets on different dates; the payment lands inside both windows.
        await SeedRemindedInstallmentAsync(
            context, fixture.AgencyAId, tag, new DateOnly(2027, 2, 12), 2_000_000m,
            [(7, new DateOnly(2027, 2, 5)), (3, new DateOnly(2027, 2, 9))]);

        var report = await client.GetFromJsonAsync<SmsEffectivenessReportDto>(
            Url(new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 28)));

        Assert.Equal(2, report!.OffsetRows.Count);
        var offset7 = Assert.Single(report.OffsetRows, r => r.OffsetDays == 7);
        var offset3 = Assert.Single(report.OffsetRows, r => r.OffsetDays == 3);
        Assert.Equal(1, offset7.Sent);
        Assert.Equal(1, offset7.Paid);   // 2027-02-12 is within 7 of both sends.
        Assert.Equal(1, offset3.Paid);
        Assert.Equal(100m, offset7.Rate);

        var monthRow = Assert.Single(report.MonthRows);
        Assert.Equal(2, monthRow.Sent);
        Assert.Equal(2, monthRow.Paid);
        Assert.Equal(100m, monthRow.Rate);
    }

    [Fact]
    public async Task Gap_months_between_active_months_are_zero_filled()
    {
        var fixture = await GetOrCreateFixtureAsync();
        var client = await LoginForAsync(fixture, fixture.AgencyAId);
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;

        var tag = Guid.NewGuid().ToString("N")[..6];
        // 2027-04 and 2027-06 are Jalali Farvardin and Khordad 1406 — Ordibehesht between them
        // has no sends. A missing month must read as zero, not as missing data (rule 16).
        await SeedRemindedInstallmentAsync(
            context, fixture.AgencyAId, $"{tag}-farvardin", new DateOnly(2027, 4, 12), 1_000_000m,
            [(7, new DateOnly(2027, 4, 5))]);
        await SeedRemindedInstallmentAsync(
            context, fixture.AgencyAId, $"{tag}-khordad", new DateOnly(2027, 6, 12), 1_000_000m,
            [(7, new DateOnly(2027, 6, 5))]);

        var report = await client.GetFromJsonAsync<SmsEffectivenessReportDto>(
            Url(new DateOnly(2027, 4, 1), new DateOnly(2027, 6, 30)));

        Assert.Equal(3, report!.MonthRows.Count);
        Assert.Equal(0, report.MonthRows[1].Sent);
        Assert.Equal(0, report.MonthRows[1].Paid);
        Assert.Equal(0m, report.MonthRows[1].Rate);
        Assert.Equal(1, report.MonthRows[0].Sent);
        Assert.Equal(1, report.MonthRows[2].Sent);
    }

    [Fact]
    public async Task Renewal_and_marketer_logs_never_enter_the_report()
    {
        var fixture = await GetOrCreateFixtureAsync();
        var client = await LoginForAsync(fixture, fixture.AgencyAId);
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;

        var tag = Guid.NewGuid().ToString("N")[..6];
        await SeedRemindedInstallmentAsync(
            context, fixture.AgencyAId, $"{tag}-real", new DateOnly(2027, 7, 11), 1_000_000m,
            [(7, new DateOnly(2027, 7, 5))]);

        // A renewal-watch log (InstallmentId null) and a marketer log (RecipientType Marketer) —
        // both in-window noise the attribution query must skip.
        context.ReminderLogs.AddRange(
            new ReminderLog
            {
                AgencyId = fixture.AgencyAId,
                RecipientType = ReminderRecipientType.Customer,
                Mobile = "09120000000",
                OffsetDays = 7,
                TemplateKey = "renewal-watch-v1",
                Channel = ReminderChannel.Sms,
                Status = ReminderSendStatus.Sent,
                SentAt = new DateTimeOffset(new DateOnly(2027, 7, 6).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            },
            new ReminderLog
            {
                AgencyId = fixture.AgencyAId,
                RecipientType = ReminderRecipientType.Marketer,
                Mobile = "09120000001",
                OffsetDays = 0,
                TemplateKey = "marketer-digest-v1",
                Channel = ReminderChannel.Sms,
                Status = ReminderSendStatus.Sent,
                SentAt = new DateTimeOffset(new DateOnly(2027, 7, 7).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            });
        await context.SaveChangesAsync();

        var report = await client.GetFromJsonAsync<SmsEffectivenessReportDto>(
            Url(new DateOnly(2027, 7, 1), new DateOnly(2027, 7, 31)));

        var summary = report!.Summary;
        Assert.Equal(1, summary.RemindersSent);
        Assert.Equal(1, summary.PaidWithinWindow);
    }

    [Fact]
    public async Task Details_rows_show_days_to_pay_and_paginate()
    {
        var fixture = await GetOrCreateFixtureAsync();
        var client = await LoginForAsync(fixture, fixture.AgencyAId);
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;

        var tag = Guid.NewGuid().ToString("N")[..6];
        // Same-day payment → DaysToPay 0; a later one → 5.
        await SeedRemindedInstallmentAsync(
            context, fixture.AgencyAId, $"{tag}-today", new DateOnly(2027, 8, 5), 1_000_000m,
            [(7, new DateOnly(2027, 8, 5))]);
        await SeedRemindedInstallmentAsync(
            context, fixture.AgencyAId, $"{tag}-later", new DateOnly(2027, 8, 10), 1_000_000m,
            [(7, new DateOnly(2027, 8, 5))]);

        var from = new DateOnly(2027, 8, 1);
        var to = new DateOnly(2027, 8, 31);
        var page = await client.GetFromJsonAsync<SmsEffectivenessDetailPageDto>($"{DetailsUrl(from, to)}&page=1&pageSize=50");

        var rows = page!.Rows.Where(r => r.CustomerFullName.Contains(tag)).ToList();
        Assert.Equal(2, rows.Count);
        var today = Assert.Single(rows, r => r.CustomerFullName.Contains("today"));
        var later = Assert.Single(rows, r => r.CustomerFullName.Contains("later"));
        Assert.Equal(0, today.DaysToPay);
        Assert.Equal(5, later.DaysToPay);
        Assert.Equal(1, today.ReminderCount);
        Assert.Equal("7", today.Offsets);
        Assert.NotNull(today.FirstPaidOnAfterReminder);
        Assert.Equal(1_000_000m, today.CollectedInWindowToman);

        var pageOne = await client.GetFromJsonAsync<SmsEffectivenessDetailPageDto>(
            $"{DetailsUrl(from, to)}&page=1&pageSize=1");
        Assert.Single(pageOne!.Rows);
    }

    [Fact]
    public async Task Attribution_window_of_one_day_changes_the_verdict()
    {
        var fixture = await GetOrCreateFixtureAsync();
        var client = await LoginForAsync(fixture, fixture.AgencyAId);
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;

        var tag = Guid.NewGuid().ToString("N")[..6];
        // Payment 4 days after the send: effective at N=7, not at N=1.
        await SeedRemindedInstallmentAsync(
            context, fixture.AgencyAId, tag, new DateOnly(2027, 9, 9), 1_000_000m,
            [(7, new DateOnly(2027, 9, 5))]);

        var from = new DateOnly(2027, 9, 1);
        var to = new DateOnly(2027, 9, 30);
        var withSeven = await client.GetFromJsonAsync<SmsEffectivenessReportDto>(Url(from, to, 7));
        var withOne = await client.GetFromJsonAsync<SmsEffectivenessReportDto>(Url(from, to, 1));

        Assert.Equal(1, withSeven!.Summary.RemindersSent);
        Assert.Equal(1, withSeven.Summary.PaidWithinWindow);
        Assert.Equal(1_000_000m, withSeven.Summary.AttributedCollectedToman);
        Assert.Equal(0, withOne!.Summary.PaidWithinWindow);
        Assert.Equal(0m, withOne.Summary.AttributedCollectedToman);
    }

    [Fact]
    public async Task Export_returns_an_xlsx_file()
    {
        var fixture = await GetOrCreateFixtureAsync();
        var client = await LoginForAsync(fixture, fixture.AgencyAId);
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;

        var tag = Guid.NewGuid().ToString("N")[..6];
        await SeedRemindedInstallmentAsync(
            context, fixture.AgencyAId, tag, new DateOnly(2027, 10, 11), 1_000_000m,
            [(7, new DateOnly(2027, 10, 5))]);

        var response = await client.GetAsync(
            Url(new DateOnly(2027, 10, 1), new DateOnly(2027, 10, 31)).Replace(
                "effectiveness-report?", "effectiveness-report/export?"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AgencyB_data_is_invisible_to_agencyA_under_rls()
    {
        var fixture = await GetOrCreateFixtureAsync();
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyBId;

        var tag = Guid.NewGuid().ToString("N")[..6];
        // Agency B's own reminded-and-paid installment.
        await SeedRemindedInstallmentAsync(
            context, fixture.AgencyBId, $"{tag}-b", new DateOnly(2027, 11, 11), 1_000_000m,
            [(7, new DateOnly(2027, 11, 5))]);

        var from = new DateOnly(2027, 11, 1);
        var to = new DateOnly(2027, 11, 30);

        var clientB = await LoginForAsync(fixture, fixture.AgencyBId);
        var reportB = await clientB.GetFromJsonAsync<SmsEffectivenessReportDto>(Url(from, to));
        Assert.Equal(1, reportB!.Summary.RemindersSent);
        Assert.Equal(1, reportB.Summary.PaidWithinWindow);

        var clientA = await LoginForAsync(fixture, fixture.AgencyAId);
        var reportA = await clientA.GetFromJsonAsync<SmsEffectivenessReportDto>(Url(from, to));
        Assert.Equal(0, reportA!.Summary.RemindersSent);
        Assert.Equal(0, reportA.Summary.DistinctInstallments);
    }

    [Fact]
    public async Task Invalid_range_and_attribution_days_answer_400_in_persian()
    {
        var fixture = await GetOrCreateFixtureAsync();
        var client = await LoginForAsync(fixture, fixture.AgencyAId);

        var badRange = await client.GetAsync(Url(new DateOnly(2027, 1, 30), new DateOnly(2027, 1, 1)));
        Assert.Equal(HttpStatusCode.BadRequest, badRange.StatusCode);
        // Deserialized, not raw: the JSON config escapes Persian as \uXXXX, so Contains on the raw
        // string would fail even though the title is Persian. Ordinal — the title's «بازهٔ» carries a
        // combining hamza and the culture comparer folds it differently than the literal.
        var body = await badRange.Content.ReadFromJsonAsync<ProblemDetailsLite>();
        Assert.NotNull(body!.Title);
        Assert.StartsWith("بازه", body.Title, StringComparison.Ordinal);

        var badDays = await client.GetAsync(Url(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 30), 45));
        Assert.Equal(HttpStatusCode.BadRequest, badDays.StatusCode);
        var daysBody = await badDays.Content.ReadFromJsonAsync<ProblemDetailsLite>();
        Assert.NotNull(daysBody!.Title);
        Assert.StartsWith("روزهای", daysBody.Title, StringComparison.Ordinal);

        var detailsBadDays = await client.GetAsync($"{DetailsUrl(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 30), 99)}&page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.BadRequest, detailsBadDays.StatusCode);
    }

    private sealed record ProblemDetailsLite(string? Title);
}
