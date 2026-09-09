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

namespace Aqsat.UnitTests.InputValidation;

/// <summary>
/// The user-error review's Critical/High fixes: entry-mistake guards on issuance (service fee,
/// commission percent, date window), the shared PaidOn window on payment recording, Persian-digit
/// mobile login normalization, and Persian model-binding messages. Each test names the exact user
/// mistake it protects against — a wrong field order, a Jalali/Gregorian year mixup, a future
/// payment date, a Persian keyboard on the login form, an empty login body.
/// </summary>
[Collection("WebApplicationFactory")]
public class InputValidationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public InputValidationEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Negative_service_fee_is_rejected_with_a_persian_reason()
    {
        var (client, lineId) = await CreateClientWithLineAsync();
        var response = await PostCreatePolicyAsync(client, lineId, serviceFee: -50_000m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("کارمزد خدمات", problem!.Title);
    }

    [Fact]
    public async Task Service_fee_larger_than_premium_is_rejected()
    {
        var (client, lineId) = await CreateClientWithLineAsync();
        var response = await PostCreatePolicyAsync(client, lineId, serviceFee: 20_000_000m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("کارمزد خدمات", problem!.Title);
    }

    [Fact]
    public async Task Out_of_range_agency_commission_percent_is_rejected()
    {
        var (client, lineId) = await CreateClientWithLineAsync();
        var response = await PostCreatePolicyAsync(client, lineId, agencyCommissionPercent: 150m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("کمیسیون", problem!.Title);
    }

    [Fact]
    public async Task End_date_on_or_before_start_date_is_rejected()
    {
        var (client, lineId) = await CreateClientWithLineAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await PostCreatePolicyAsync(client, lineId, startDate: today, endDate: today);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("تاریخ پایان", problem!.Title);
    }

    [Fact]
    public async Task A_start_date_two_years_in_the_past_is_rejected()
    {
        // The classic Jalali/Gregorian mixup: 1404 typed where 2026 belongs, or a stale year in
        // a picker — the schedule would silently generate due dates nobody's countdown shows.
        var (client, lineId) = await CreateClientWithLineAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await PostCreatePolicyAsync(client, lineId,
            startDate: today.AddYears(-2), endDate: today.AddYears(-1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("تاریخ شروع", problem!.Title);
    }

    [Fact]
    public async Task A_wellformed_policy_still_passes_the_new_guards()
    {
        var (client, lineId) = await CreateClientWithLineAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await PostCreatePolicyAsync(client, lineId,
            startDate: today, endDate: today.AddYears(1), serviceFee: 200_000m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_future_payment_date_is_rejected()
    {
        var (client, installmentId) = await SeedScheduledPolicyAsync();
        var response = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installmentId, 100_000m, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), "Cash", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("تاریخ پرداخت", problem!.Title);
    }

    [Fact]
    public async Task A_payment_date_far_in_the_past_is_rejected()
    {
        var (client, installmentId) = await SeedScheduledPolicyAsync();
        var response = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installmentId, 100_000m, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-3), "Cash", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("تاریخ پرداخت", problem!.Title);
    }

    [Fact]
    public async Task A_today_payment_date_still_passes()
    {
        var (client, installmentId) = await SeedScheduledPolicyAsync();
        var response = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installmentId, 100_000m, DateOnly.FromDateTime(DateTime.UtcNow), "Cash", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_accepts_a_persian_digit_mobile()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        var persianMobile = string.Concat(fixture.DualAgencyManagerMobile.Select(ToPersianDigit));
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(persianMobile, DevSeeder.SeededUserPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Default_model_validation_errors_come_back_in_persian()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("field is required", json);
        Assert.Contains("درخواست ارسالی نامعتبر است", json);
    }

    private static char ToPersianDigit(char ch) => ch is >= '0' and <= '9' ? (char)('۰' + ch - '0') : ch;

    private async Task<(HttpClient Client, Guid LineId)> CreateClientWithLineAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var lineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var client = await LoginAsync(fixture);
        return (client, lineId);
    }

    private static Task<HttpResponseMessage> PostCreatePolicyAsync(
        HttpClient client, Guid lineId,
        DateOnly? startDate = null, DateOnly? endDate = null,
        decimal serviceFee = 0m, decimal? agencyCommissionPercent = null)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{Guid.NewGuid():N}"[..16],
            lineId,
            null, $"مشتری {Guid.NewGuid():N}"[..10], null, null,
            new VehicleInput("۱۱الف۱۱۱", null, null, null, null, null),
            null,
            today,
            startDate ?? today,
            endDate ?? today.AddYears(1),
            10_000_000m,
            serviceFee,
            null, null, false,
            AgencyCommissionPercent: agencyCommissionPercent));
    }

    /// <summary>The payment-date tests don't care about schedule generation — a directly seeded
    /// unpaid installment is all the endpoint needs to reach the PaidOn guard.</summary>
    private async Task<(HttpClient Client, Guid InstallmentId)> SeedScheduledPolicyAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var lineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        // RLS: the seed context inserts through the block predicate, so the agency scope must be
        // set before the first SaveChanges.
        AgencyContext.Current = fixture.AgencyAId;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var customer = new Customer { AgencyId = fixture.AgencyAId, ExternalCode = $"EXT-{Guid.NewGuid():N}"[..12], FullName = "مشتری پرداخت" };
        var vehicle = new Vehicle { AgencyId = fixture.AgencyAId, Plate = "44د444" };
        seedContext.Customers.Add(customer);
        seedContext.Vehicles.Add(vehicle);
        await seedContext.SaveChangesAsync();

        var policy = new Policy
        {
            AgencyId = fixture.AgencyAId,
            PolicyNumber = $"POL-PAY-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = lineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = today,
            StartDate = today,
            EndDate = today.AddYears(1),
            NetPremium = 10_000_000m,
            DownPayment = 0,
            InstallmentCount = 1,
        };
        seedContext.Policies.Add(policy);
        await seedContext.SaveChangesAsync();
        var installment = new Installment
        {
            AgencyId = fixture.AgencyAId,
            PolicyId = policy.Id,
            SeqNo = 1,
            DueDate = today.AddMonths(1),
            SettlementDeadline = today.AddMonths(1).AddDays(3),
            Amount = 10_000_000m,
            Status = InstallmentStatus.Unpaid,
        };
        seedContext.Installments.Add(installment);
        await seedContext.SaveChangesAsync();

        var client = await LoginAsync(fixture);
        return (client, installment.Id);
    }

    private async Task<HttpClient> LoginAsync(DevSeeder.SeededAuthFixture fixture)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());
        return client;
    }

    private sealed record ProblemDetailsDto(string Title);
}
