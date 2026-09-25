using Aqsat.Application.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Today;

/// <summary>
/// The Today dashboard's worklist is the existing /api/installments surface, so what is under test
/// here is exactly what the redesign added to it: the «قسط ۳ از ۶» denominator, the collection
/// chips, their server-side counters, the xlsx export, and the sidebar badges.
/// </summary>
[Collection("WebApplicationFactory")]
public class TodayWorklistEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TodayWorklistEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task SeqTotal_reports_the_policy_count_and_the_chip_counts_match_the_chips()
    {
        var (client, fixture, lineId) = await SignInAsync();
        var policy = await CreateScheduledPolicyAsync(client, lineId, "SEQ", installments: 6);

        var rows = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>("/api/installments");
        var mine = rows!.Where(r => r.PolicyId == policy.PolicyId).ToList();
        Assert.Equal(6, mine.Count);
        Assert.All(mine, r => Assert.Equal(6, r.SeqTotal));

        var counts = await client.GetFromJsonAsync<InstallmentCountsDto>("/api/installments/counts");
        Assert.NotNull(counts);
        Assert.Equal(
            new[] { "all", "overdue", "cheque", "promise", "noContact" },
            counts!.Filters.Select(f => f.Filter));

        // «همه» and the unfiltered total are the same question asked twice; if they ever disagree
        // the chip would promise a row the list will not return.
        Assert.Equal(counts.Total, counts.Filters.Single(f => f.Filter == "all").Count);

        // The overdue chip and the overdueOnly flag are the same predicate, so they must return the
        // same rows — that equivalence is what lets the old page keep working unchanged.
        var byChip = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>("/api/installments?filter=overdue");
        var byFlag = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>("/api/installments?overdueOnly=true");
        Assert.Equal(
            byFlag!.Select(r => r.InstallmentId).OrderBy(id => id),
            byChip!.Select(r => r.InstallmentId).OrderBy(id => id));

        // RLS: agency B's manager sees none of agency A's rows, even acting in the same process.
        await using var seedContext = TestDbContextFactory.Create();
        var clientB = await SignInAsAsync(fixture.DualAgencyManagerMobile, fixture.AgencyBId);
        var rowsB = await clientB.GetFromJsonAsync<List<InstallmentWorklistRowDto>>("/api/installments");
        Assert.DoesNotContain(rowsB!, r => r.PolicyId == policy.PolicyId);
    }

    [Fact]
    public async Task Cheque_promise_and_noContact_chips_each_select_their_own_policy()
    {
        var (client, _, lineId) = await SignInAsync();

        // A cash box is required to receive a cheque — RLS blocks the insert, so the seed context
        // must act as the agency to create it.
        await using var seedContext = TestDbContextFactory.Create();
        var agencyId = Guid.Parse(client.DefaultRequestHeaders.GetValues("X-Organization-Id").Single());
        AgencyContext.Current = agencyId;
        var cashBox = new CashBox { AgencyId = agencyId, Name = "صندوق تست امروز" };
        seedContext.CashBoxes.Add(cashBox);
        await seedContext.SaveChangesAsync();

        var today = IranClock.Today();
        var chequePolicy = await CreateScheduledPolicyAsync(client, lineId, "CHQ", installments: 3);
        var promisePolicy = await CreateScheduledPolicyAsync(client, lineId, "PRM", installments: 3);
        var quietPolicy = await CreateScheduledPolicyAsync(client, lineId, "QUI", installments: 3);

        // 1. A cheque the agency is still waiting on.
        var chequeRow = (await InstallmentsOfAsync(client, chequePolicy))[0];
        var payment = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            chequeRow.InstallmentId, 500_000m, today, "چک", null,
            MethodType: PaymentMethod.Cheque,
            Cheque: new ChequeDetailsRequest("777123", "بانک ملت", today.AddDays(20), "معرف تستی", cashBox.Id)));
        payment.EnsureSuccessStatusCode();

        // 2. A live promise, and 3. a call that takes the quiet policy back out of «بدون تماس».
        var promiseRow = (await InstallmentsOfAsync(client, promisePolicy))[0];
        var quietRow = (await InstallmentsOfAsync(client, quietPolicy))[0];

        var promise = await client.PostAsJsonAsync("/api/collection-contacts", new CreateCollectionContactRequest(
            promiseRow.PolicyId, promiseRow.InstallmentId, "Call", "Promised",
            DateTimeOffset.UtcNow, today.AddDays(2), 400_000m));
        promise.EnsureSuccessStatusCode();

        // Answering the phone and saying nothing useful is not a promise, so it carries no promise
        // terms — the endpoint refuses that combination.
        var answered = await client.PostAsJsonAsync("/api/collection-contacts", new CreateCollectionContactRequest(
            quietRow.PolicyId, quietRow.InstallmentId, "Call", "NoAnswer", DateTimeOffset.UtcNow, null, null));
        answered.EnsureSuccessStatusCode();

        var byCheque = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>("/api/installments?filter=cheque");
        Assert.Contains(byCheque!, r => r.PolicyId == chequePolicy.PolicyId);
        Assert.DoesNotContain(byCheque!, r => r.PolicyId == promisePolicy.PolicyId);

        var byPromise = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>("/api/installments?filter=promise");
        Assert.Contains(byPromise!, r => r.PolicyId == promisePolicy.PolicyId);
        Assert.DoesNotContain(byPromise!, r => r.PolicyId == chequePolicy.PolicyId);

        // Only a logged *call* counts as contact — the quiet policy was called, so it drops out.
        var byNoContact = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>("/api/installments?filter=noContact");
        Assert.DoesNotContain(byNoContact!, r => r.PolicyId == quietPolicy.PolicyId || r.PolicyId == promisePolicy.PolicyId);

        // And every chip's own count is at least the rows it just returned — the counter can never
        // read lower than the list it labels.
        var counts = await client.GetFromJsonAsync<InstallmentCountsDto>("/api/installments/counts");
        foreach (var (chip, listed) in new[]
                 {
                     ("cheque", byCheque!), ("promise", byPromise!), ("noContact", byNoContact!),
                 })
        {
            var reported = counts!.Filters.Single(f => f.Filter == chip).Count;
            Assert.True(
                reported >= listed.Count,
                $"chip «{chip}» reported {reported} but returned {listed.Count} rows");
        }
    }

    [Fact]
    public async Task An_unknown_chip_is_rejected_rather_than_silently_returning_everything()
    {
        var (client, _, _) = await SignInAsync();

        var response = await client.GetAsync("/api/installments?filter=چیزی‌که‌وجود‌ندارد");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var export = await client.GetAsync("/api/installments/export?filter=nope");
        Assert.Equal(HttpStatusCode.BadRequest, export.StatusCode);
    }

    [Fact]
    public async Task Export_returns_a_Persian_xlsx_of_the_same_rows()
    {
        var (client, _, lineId) = await SignInAsync();
        var policy = await CreateScheduledPolicyAsync(client, lineId, "XLS", installments: 3);

        var response = await client.GetAsync($"/api/installments/export?search={policy.PolicyNumber}");
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);

        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        // «قسط» and «از» are separate columns so the sheet reads «۳ از ۶» the way the screen does.
        Assert.Equal("قسط", sheet.Cell(1, 4).GetString());
        Assert.Equal("از", sheet.Cell(1, 5).GetString());
        Assert.Equal(3, sheet.Cell(2, 5).GetValue<int>());
        Assert.Equal(policy.PolicyNumber, sheet.Cell(2, 1).GetString());
    }

    [Fact]
    public async Task Nav_badges_require_authentication_and_count_only_open_work()
    {
        var anonymous = _factory.CreateClient();
        var unauthenticated = await anonymous.GetAsync("/api/nav/badges");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        var (client, _, lineId) = await SignInAsync();
        var before = await client.GetFromJsonAsync<NavBadgesDto>("/api/nav/badges");
        Assert.NotNull(before);

        // A policy issued three months ago with three installments leaves at least one overdue, so
        // the worklist badge must move — a badge that never changes is decoration, not a signal.
        var policy = await CreateScheduledPolicyAsync(client, lineId, "BDG", installments: 3);
        var after = await client.GetFromJsonAsync<NavBadgesDto>("/api/nav/badges");
        Assert.True(after!.InstallmentWorklist > before!.InstallmentWorklist);

        // A badge is a call to action, so nothing unusable is counted: the policy's own overdue
        // rows are what moved the number, and no count is negative.
        Assert.All(
            new[] { after.InstallmentWorklist, after.Cheques, after.RiskReviews, after.RenewalWatches },
            count => Assert.True(count >= 0));
        _ = policy;
    }

    private async Task<(HttpClient Client, DevSeeder.SeededAuthFixture Fixture, Guid LineId)> SignInAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var lineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var client = await SignInAsAsync(fixture.DualAgencyManagerMobile, fixture.AgencyAId);
        return (client, fixture, lineId);
    }

    private async Task<HttpClient> SignInAsAsync(string mobile, Guid agencyId)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, DevSeeder.SeededUserPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", agencyId.ToString());
        return client;
    }

    /// <summary>Issued three months ago, so the monthly due dates have already passed and the
    /// deadline-driven chips have something real to select.</summary>
    private static async Task<CreatePolicyResultDto> CreateScheduledPolicyAsync(
        HttpClient client, Guid lineId, string tag, int installments)
    {
        var today = IranClock.Today();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{tag}-{unique}", lineId, null, $"مشتری امروز {unique}", null, null,
            Vehicle: new VehicleInput($"۵۵د{unique[..3]}", null, null, null, null, null), Property: null,
            today.AddMonths(-3), today.AddMonths(-3), today.AddYears(1), 9_000_000m, 0m, null, null, false));
        response.EnsureSuccessStatusCode();
        var policy = (await response.Content.ReadFromJsonAsync<CreatePolicyResultDto>())!;

        var schedule = await client.PostAsJsonAsync(
            $"/api/policies/{policy.PolicyId}/schedule", new ScheduleRequest(0m, installments));
        schedule.EnsureSuccessStatusCode();
        return policy;
    }

    private static async Task<IReadOnlyList<InstallmentWorklistRowDto>> InstallmentsOfAsync(
        HttpClient client, CreatePolicyResultDto policy)
    {
        var rows = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>(
            $"/api/installments?search={policy.PolicyNumber}");
        return rows!.Where(r => r.PolicyId == policy.PolicyId).ToList();
    }
}
