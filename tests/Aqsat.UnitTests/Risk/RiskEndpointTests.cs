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

namespace Aqsat.UnitTests.Risk;

/// <summary>
/// docs Phase 2A §20/§29 — the risk endpoints over the real pipeline: manual assessment, history,
/// the credit-limit override, per-agency settings, and RLS (a customer outside the caller's scope
/// is a 404, never a silent empty result — rule 17).
/// </summary>
[Collection("WebApplicationFactory")]
public class RiskEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RiskEndpointTests(WebApplicationFactory<Program> factory)
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

    /// <summary>A customer with one active installment policy — enough evidence for a real score,
    /// with nothing overdue and no settled installments yet (a fresh installment customer).</summary>
    private static async Task<Guid> SeedCustomerWithPolicyAsync(Guid agencyId)
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
            ExternalCode = $"RISK-{Guid.NewGuid():N}"[..16],
            FullName = "مشتری اعتبارسنجی",
            NationalId = UniqueNationalId(),
            Mobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}",
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var policy = new Policy
        {
            AgencyId = agencyId,
            PolicyNumber = $"RISK-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = lineId,
            CustomerId = customer.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)),
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            NetPremium = 8_000_000,
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
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            SettlementDeadline = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1).AddDays(3)),
            Amount = 2_000_000,
            Status = InstallmentStatus.Unpaid,
        });
        await context.SaveChangesAsync();

        return customer.Id;
    }

    [Fact]
    public async Task Assessing_a_customer_without_evidence_reports_insufficient_data()
    {
        var (client, fixture) = await LoginAsync();
        AgencyContext.Current = fixture.AgencyAId;
        await using var context = TestDbContextFactory.Create();
        var customer = new Customer
        {
            AgencyId = fixture.AgencyAId,
            ExternalCode = $"RISK-{Guid.NewGuid():N}"[..16],
            FullName = "مشتری بدون سابقه",
            NationalId = UniqueNationalId(),
            Mobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}",
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var assess = await client.PostAsJsonAsync($"/api/customers/{customer.Id}/risk/assess", new { });
        assess.EnsureSuccessStatusCode();
        var dto = await assess.Content.ReadFromJsonAsync<CustomerRiskDto>();
        Assert.False(dto!.HasAssessment);
        Assert.True(dto.InsufficientData);
        Assert.Null(dto.Latest);

        var risk = await client.GetAsync($"/api/customers/{customer.Id}/risk");
        risk.EnsureSuccessStatusCode();
        var riskDto = await risk.Content.ReadFromJsonAsync<CustomerRiskDto>();
        Assert.False(riskDto!.HasAssessment);
    }

    [Fact]
    public async Task Assessing_a_customer_with_a_policy_produces_a_score_history_and_audit()
    {
        var (client, fixture) = await LoginAsync();
        var customerId = await SeedCustomerWithPolicyAsync(fixture.AgencyAId);

        var assess = await client.PostAsJsonAsync($"/api/customers/{customerId}/risk/assess", new { });
        assess.EnsureSuccessStatusCode();
        var dto = await assess.Content.ReadFromJsonAsync<CustomerRiskDto>();
        Assert.True(dto!.HasAssessment);
        Assert.InRange(dto.Latest!.Score, 0, 1000);
        Assert.True(dto.Latest.CreditLimitToman > 0);
        Assert.NotEmpty(dto.Latest.Factors);

        var history = await client.GetAsync($"/api/customers/{customerId}/risk/history");
        history.EnsureSuccessStatusCode();
        var items = await history.Content.ReadFromJsonAsync<List<RiskHistoryItemDto>>();
        Assert.Single(items!);
        Assert.Equal(dto.Latest.Score, items![0].Score);

        var limit = await client.GetAsync($"/api/customers/{customerId}/credit-limit");
        limit.EnsureSuccessStatusCode();
        var limitDto = await limit.Content.ReadFromJsonAsync<CustomerCreditLimitDto>();
        Assert.Null(limitDto!.OverrideToman);
        Assert.Equal(dto.Latest.CreditLimitToman, limitDto.EffectiveToman);

        // The assessment wrote its audit row in the same transaction (rule 29), keyed to the
        // customer subject with PolicyId = Guid.Empty (no policy is the audit subject here).
        AgencyContext.Current = fixture.AgencyAId;
        await using var verify = TestDbContextFactory.Create();
        Assert.True(await verify.AuditEntries.AsNoTracking().AnyAsync(a =>
            a.AgencyId == fixture.AgencyAId &&
            a.EntityType == "RiskAssessment" &&
            a.EntityId == dto.Latest.Id &&
            a.PolicyId == Guid.Empty));
    }

    [Fact]
    public async Task A_credit_limit_override_round_trips_and_beats_the_recommendation()
    {
        var (client, fixture) = await LoginAsync();
        var customerId = await SeedCustomerWithPolicyAsync(fixture.AgencyAId);

        var put = await client.PutAsJsonAsync(
            $"/api/customers/{customerId}/credit-limit",
            new SetCustomerCreditLimitRequest(50_000_000m, "تسهیلات ویژه"));
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var limit = await client.GetAsync($"/api/customers/{customerId}/credit-limit");
        limit.EnsureSuccessStatusCode();
        var dto = await limit.Content.ReadFromJsonAsync<CustomerCreditLimitDto>();
        Assert.Equal(50_000_000m, dto!.OverrideToman);
        Assert.Equal(50_000_000m, dto.EffectiveToman);
    }

    [Fact]
    public async Task A_negative_credit_limit_is_refused()
    {
        var (client, fixture) = await LoginAsync();
        var customerId = await SeedCustomerWithPolicyAsync(fixture.AgencyAId);

        var put = await client.PutAsJsonAsync(
            $"/api/customers/{customerId}/credit-limit",
            new SetCustomerCreditLimitRequest(-1m, null));
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Another_agency_sees_nothing_of_the_customer()
    {
        var (clientA, fixture) = await LoginAsync();
        var customerId = await SeedCustomerWithPolicyAsync(fixture.AgencyAId);

        var clientB = _factory.CreateClient();
        var loginB = await clientB.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginB.EnsureSuccessStatusCode();
        var tokenB = (await loginB.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        clientB.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyBId.ToString());

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/customers/{customerId}/risk")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/customers/{customerId}/risk/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/customers/{customerId}/credit-limit")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await clientB.PostAsJsonAsync($"/api/customers/{customerId}/risk/assess", new { })).StatusCode);
    }

    [Fact]
    public async Task Settings_default_to_the_documented_values_and_reject_a_broken_weight_sum()
    {
        var (client, _) = await LoginAsync();

        var get = await client.GetAsync("/api/risk/settings");
        get.EnsureSuccessStatusCode();
        var defaults = await get.Content.ReadFromJsonAsync<RiskSettingsDto>();
        Assert.Equal("Informational", defaults!.IssuanceGateMode);
        Assert.Equal(0.30m, defaults.PaymentHistoryWeight);

        // Weights that no longer sum to 1.00 are refused with the Persian reason (rule 15/16).
        var broken = defaults with { PaymentHistoryWeight = 0.10m };
        var put = await client.PutAsJsonAsync("/api/risk/settings", broken);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Contains("مجموع وزن", await put.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Settings_reject_a_negative_weight_even_when_the_sum_is_still_one()
    {
        var (client, _) = await LoginAsync();
        var get = await client.GetAsync("/api/risk/settings");
        var current = (await get.Content.ReadFromJsonAsync<RiskSettingsDto>())!;

        // A negative weight offset by a bigger one sums to exactly 1.00 but flips its factor's
        // contribution to the score — the sum check alone used to accept this.
        var poisoned = current with
        {
            PaymentHistoryWeight = -0.10m,
            CurrentDebtWeight = current.CurrentDebtWeight + 0.10m,
        };
        var put = await client.PutAsJsonAsync("/api/risk/settings", poisoned);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Contains("هر وزن عامل", await put.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Settings_updates_round_trip_per_agency()
    {
        var (client, _) = await LoginAsync();

        var get = await client.GetAsync("/api/risk/settings");
        var current = (await get.Content.ReadFromJsonAsync<RiskSettingsDto>())!;

        var put = await client.PutAsJsonAsync("/api/risk/settings", current with
        {
            IssuanceGateMode = "SoftBlock",
            BouncedChequeHighThreshold = 3,
        });
        put.EnsureSuccessStatusCode();

        var reread = await client.GetAsync("/api/risk/settings");
        var updated = await reread.Content.ReadFromJsonAsync<RiskSettingsDto>();
        Assert.Equal("SoftBlock", updated!.IssuanceGateMode);
        Assert.Equal(3, updated.BouncedChequeHighThreshold);
    }

    [Fact]
    public async Task An_unauthenticated_caller_cannot_read_risk()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/customers/{Guid.NewGuid()}/risk")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/risk/settings")).StatusCode);
    }
}
