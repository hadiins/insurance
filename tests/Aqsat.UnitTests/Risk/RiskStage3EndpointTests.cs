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

namespace Aqsat.UnitTests.Risk;

/// <summary>
/// docs Phase 2A §6/§15/§19/§20 — the agency-wide stage-3 endpoints: dashboard aggregation, the
/// merged high-risk view, the manual-review workflow (auto-open on MANUAL_REVIEW, assignment,
/// decision, no double-decide), and the warnings feed.
/// </summary>
[Collection("WebApplicationFactory")]
public class RiskStage3EndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RiskStage3EndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private async Task<(HttpClient Client, DevSeeder.SeededAuthFixture Fixture)> LoginAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());
        return (client, fixture);
    }

    private static string UniqueNationalId()
    {
        var digits = $"007{Random.Shared.Next(1_000_000):D6}";
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (digits[i] - '0') * (10 - i);
        }
        var remainder = sum % 11;
        return digits + (remainder < 2 ? remainder : 11 - remainder);
    }

    /// <summary>A customer whose one open installment is already 10 days overdue and far above the
    /// default credit limit — the assessment lands on MANUAL_REVIEW via the OVER_CREDIT_LIMIT rule.</summary>
    private static async Task<Guid> SeedReviewBandCustomerAsync(Guid agencyId)
    {
        await using var context = TestDbContextFactory.Create();
        await InsuranceLineSeeder.EnsureSeededAsync(context);
        var lineId = await context.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode)
            .Select(l => l.Id)
            .FirstAsync();

        AgencyContext.Current = agencyId;
        var customer = new Customer
        {
            AgencyId = agencyId,
            ExternalCode = $"R3-{Guid.NewGuid():N}"[..16],
            FullName = "مشتری صف بررسی",
            NationalId = UniqueNationalId(),
            Mobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}",
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var policy = new Policy
        {
            AgencyId = agencyId,
            PolicyNumber = $"R3-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = lineId,
            CustomerId = customer.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)),
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            NetPremium = 150_000_000,
            DownPayment = 0,
            InstallmentCount = 4,
            Status = PolicyStatus.Active,
        };
        context.Policies.Add(policy);
        await context.SaveChangesAsync();

        context.Installments.Add(new Installment
        {
            AgencyId = agencyId,
            PolicyId = policy.Id,
            SeqNo = 1,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            SettlementDeadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
            Amount = 150_000_000,
            Status = InstallmentStatus.Unpaid,
        });
        await context.SaveChangesAsync();

        return customer.Id;
    }

    private async Task<ManualReviewApiDto> AssessAndOpenReviewAsync(
        HttpClient client, Guid customerId, DevSeeder.SeededAuthFixture fixture)
    {
        var assess = await client.PostAsJsonAsync($"/api/customers/{customerId}/risk/assess", new { });
        assess.EnsureSuccessStatusCode();
        var dto = await assess.Content.ReadFromJsonAsync<CustomerRiskDto>();
        Assert.Equal("ManualReview", dto!.Latest!.DecisionKey);

        var reviews = await client.GetFromJsonAsync<List<ManualReviewApiDto>>("/api/risk/manual-reviews");
        return reviews!.First(r => r.CustomerId == customerId);
    }

    [Fact]
    public async Task Dashboard_aggregates_the_agency_after_an_assessment()
    {
        var (client, fixture) = await LoginAsync();
        var customerId = await SeedReviewBandCustomerAsync(fixture.AgencyAId);
        var assess = await client.PostAsJsonAsync($"/api/customers/{customerId}/risk/assess", new { });
        assess.EnsureSuccessStatusCode();

        var dashboard = await client.GetFromJsonAsync<RiskDashboardApiDto>("/api/risk/dashboard");
        Assert.NotNull(dashboard);
        Assert.True(dashboard!.TotalCustomers > 0);
        Assert.True(dashboard.AssessedCustomers >= 1);
        Assert.True(dashboard.CurrentDebtToman >= 150_000_000); // the seeded overdue installment
        Assert.True(dashboard.TotalOverdueToman >= 150_000_000);
        Assert.True(dashboard.OpenReviewsCount >= 1);
        Assert.NotNull(dashboard.ScoreTrend);
    }

    [Fact]
    public async Task High_risk_list_merges_operational_data_with_the_assessment()
    {
        var (client, fixture) = await LoginAsync();
        var customerId = await SeedReviewBandCustomerAsync(fixture.AgencyAId);
        var assess = await client.PostAsJsonAsync($"/api/customers/{customerId}/risk/assess", new { });
        assess.EnsureSuccessStatusCode();

        var rows = await client.GetFromJsonAsync<List<HighRiskRiskCustomerApiDto>>("/api/risk/high-risk");
        var row = rows!.FirstOrDefault(r => r.CustomerId == customerId);
        Assert.NotNull(row);
        Assert.Equal(1, row!.OverdueInstallmentCount);
        Assert.NotEqual(0, row.Score);
        Assert.NotNull(row.RiskLevelKey);
        Assert.NotNull(row.DecisionKey);
    }

    [Fact]
    public async Task A_manual_review_decision_opens_once_survives_reassessment_and_closes_on_decide()
    {
        var (client, fixture) = await LoginAsync();
        var customerId = await SeedReviewBandCustomerAsync(fixture.AgencyAId);

        var review = await AssessAndOpenReviewAsync(client, customerId, fixture);
        Assert.Equal("Pending", review.Status);
        Assert.NotNull(review.TriggeredRules);
        Assert.Contains(review.TriggeredRules, r => r.Code == "OVER_CREDIT_LIMIT");

        // Re-assessing the same customer refreshes the assessment, not the queue — one open case.
        var reassess = await client.PostAsJsonAsync($"/api/customers/{customerId}/risk/assess", new { });
        reassess.EnsureSuccessStatusCode();
        var reviewsAfter = await client.GetFromJsonAsync<List<ManualReviewApiDto>>("/api/risk/manual-reviews");
        Assert.Single(reviewsAfter!.Where(r => r.CustomerId == customerId));

        // Assign to the reviewing user, then approve — the case closes with an audit row.
        var assign = await client.PostAsJsonAsync(
            $"/api/risk/manual-reviews/{review.Id}/assign", new AssignReviewRequest(fixture.DualAgencyManagerId));
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);

        var decide = await client.PostAsJsonAsync(
            $"/api/risk/manual-reviews/{review.Id}/decision",
            new DecideReviewRequest("Approve", "تأیید با وثیقهٔ کافی"));
        Assert.Equal(HttpStatusCode.NoContent, decide.StatusCode);

        var closed = (await client.GetFromJsonAsync<List<ManualReviewApiDto>>("/api/risk/manual-reviews?status=Approved"))!
            .Single(r => r.Id == review.Id);
        Assert.Equal("Approved", closed.Status);
        Assert.Equal("Approve", closed.FinalDecisionKey);
        Assert.Equal("مدیر دونمایندگی", closed.AssignedToName);

        // A closed case refuses a second decision.
        var second = await client.PostAsJsonAsync(
            $"/api/risk/manual-reviews/{review.Id}/decision", new DecideReviewRequest("Decline", null));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        AgencyContext.Current = fixture.AgencyAId;
        await using var verify = TestDbContextFactory.Create();
        Assert.True(await verify.AuditEntries.AsNoTracking().AnyAsync(a =>
            a.AgencyId == fixture.AgencyAId
            && a.EntityType == "ManualReview"
            && a.EntityId == review.Id
            && a.Action == AuditAction.ManualReviewDecided));
    }

    [Fact]
    public async Task A_warning_is_listed_and_can_be_marked_read()
    {
        var (client, fixture) = await LoginAsync();
        var customerId = await SeedReviewBandCustomerAsync(fixture.AgencyAId);
        await AssessAndOpenReviewAsync(client, customerId, fixture);

        AgencyContext.Current = fixture.AgencyAId;
        await using var context = TestDbContextFactory.Create();
        var warning = new RiskWarning
        {
            Id = SequentialGuidGenerator.Next(),
            AgencyId = fixture.AgencyAId,
            CustomerId = customerId,
            AssessmentId = context.RiskAssessments.AsNoTracking()
                .Where(a => a.CustomerId == customerId)
                .OrderByDescending(a => a.CalculatedAt)
                .Select(a => a.Id)
                .First(),
            Type = RiskWarningType.LevelEscalation,
            Message = "افزایش سطح ریسک مشتری آزمایشی",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        context.RiskWarnings.Add(warning);
        await context.SaveChangesAsync();

        var listed = await client.GetFromJsonAsync<List<RiskWarningApiDto>>("/api/risk/warnings?unreadOnly=true");
        var row = listed!.Single(w => w.Id == warning.Id);
        Assert.False(row.IsRead);
        Assert.Equal("افزایش سطح ریسک", row.TypeFa);
        Assert.Equal("مشتری صف بررسی", row.CustomerName);

        var read = await client.PostAsync($"/api/risk/warnings/{warning.Id}/read", null);
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);

        var after = (await client.GetFromJsonAsync<List<RiskWarningApiDto>>("/api/risk/warnings"))!
            .Single(w => w.Id == warning.Id);
        Assert.True(after.IsRead);

        // A warning that does not exist (or belongs to another agency) is a 404, never silence.
        var missing = await client.PostAsync($"/api/risk/warnings/{Guid.NewGuid()}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Another_agency_sees_none_of_the_risk_views()
    {
        var (clientA, fixture) = await LoginAsync();
        var customerId = await SeedReviewBandCustomerAsync(fixture.AgencyAId);
        await AssessAndOpenReviewAsync(clientA, customerId, fixture);

        var clientB = _factory.CreateClient();
        var loginB = await clientB.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginB.EnsureSuccessStatusCode();
        var tokenB = (await loginB.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        clientB.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyBId.ToString());

        var highRiskB = await clientB.GetFromJsonAsync<List<HighRiskRiskCustomerApiDto>>("/api/risk/high-risk");
        Assert.DoesNotContain(highRiskB!, r => r.CustomerId == customerId);

        var reviewsB = await clientB.GetFromJsonAsync<List<ManualReviewApiDto>>("/api/risk/manual-reviews");
        Assert.DoesNotContain(reviewsB!, r => r.CustomerId == customerId);

        var warningsB = await clientB.GetFromJsonAsync<List<RiskWarningApiDto>>("/api/risk/warnings");
        Assert.DoesNotContain(warningsB!, w => w.CustomerId == customerId);
    }

    [Fact]
    public async Task An_unauthenticated_caller_cannot_read_the_risk_views()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/risk/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/risk/manual-reviews")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/risk/warnings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/risk/high-risk")).StatusCode);
    }
}
