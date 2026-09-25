using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Common;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Customers;

/// <summary>
/// Owner decision 2026-09-21 — foreign nationals: a 996-series resident registers through the
/// same endpoints as an Iranian (their ID shares the NationalId column), while a passport-only
/// customer registers with a passport and is complete without any national ID. External credit
/// inquiries never run for the passport-only kind (PolicyVerificationService checks Kind).
/// </summary>
[Collection("WebApplicationFactory")]
public class ForeignNationalCustomerEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ForeignNationalCustomerEndpointTests(WebApplicationFactory<Program> factory)
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

    private static string Unique996Id() => $"996{Random.Shared.Next(10_000_000):D7}"; // 3+7 = 10 digits

    private static string UniquePassport() => $"P{Random.Shared.Next(100000, 999999)}X";

    [Fact]
    public async Task A_996_resident_registers_through_the_normal_flow()
    {
        var (client, fixture) = await LoginAsync();
        var nationalId = Unique996Id();

        var response = await client.PostAsJsonAsync("/api/customers", new CreateCustomerRequest(
            "اتباع", "دارای کدملی", nationalId, "09123334455", null, null, null,
            CustomerKind.ForeignResident));
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<CustomerLookupResultDto>();
        Assert.True(dto!.Found);
        Assert.Equal(nationalId, dto.Customer!.NationalId);

        // The wizard's step-1 lookup finds a 996 customer by the same key.
        var lookup = await client.GetAsync($"/api/customers/lookup?nationalId={nationalId}");
        lookup.EnsureSuccessStatusCode();
        var found = await lookup.Content.ReadFromJsonAsync<CustomerLookupResultDto>();
        Assert.True(found!.Found);
        Assert.Equal(CustomerKind.ForeignResident, found.Customer!.Kind);

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var row = await verify.Customers.AsNoTracking().FirstAsync(c => c.Id == dto.Customer.Id);
        Assert.Equal(CustomerKind.ForeignResident, row.Kind);
        Assert.NotNull(row.NationalIdHash);
    }

    [Fact]
    public async Task A_996_id_with_an_invalid_shape_is_refused()
    {
        var (client, _) = await LoginAsync();

        // Kind says ForeignResident but the ID is a regular Iranian-series number.
        var wrongShape = await client.PostAsJsonAsync("/api/customers", new CreateCustomerRequest(
            "اتباع", "سری اشتباه", "0072345454", "09123334455", null, null, null,
            CustomerKind.ForeignResident));
        Assert.Equal(HttpStatusCode.BadRequest, wrongShape.StatusCode);
        Assert.Contains("۹۹۶", await wrongShape.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_passport_only_customer_registers_and_completes_without_a_national_id()
    {
        var (client, fixture) = await LoginAsync();
        var passport = UniquePassport();

        var response = await client.PostAsJsonAsync("/api/customers", new CreateCustomerRequest(
            "اتباع", "پاسپورتی", null, "09123334466", null, null, null,
            CustomerKind.ForeignPassportOnly, passport, new DateOnly(2029, 5, 20)));
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<CustomerLookupResultDto>();
        Assert.True(dto!.Found);
        Assert.Null(dto.Customer!.NationalId);

        // The wizard looks a passport-only customer up by passport.
        var lookup = await client.GetAsync($"/api/customers/lookup?passportNumber={passport}");
        lookup.EnsureSuccessStatusCode();
        var found = await lookup.Content.ReadFromJsonAsync<CustomerLookupResultDto>();
        Assert.True(found!.Found);
        Assert.Equal(CustomerKind.ForeignPassportOnly, found.Customer!.Kind);
        Assert.Equal(passport, found.Customer!.PassportNumber);

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var row = await verify.Customers.AsNoTracking().FirstAsync(c => c.Id == dto.Customer.Id);
        Assert.Equal(CustomerKind.ForeignPassportOnly, row.Kind);
        Assert.Equal("2029-05-20", row.PassportExpiry!.Value.ToString("yyyy-MM-dd"));
        Assert.Null(row.NationalId);
    }

    [Fact]
    public async Task A_duplicate_passport_is_refused()
    {
        var (client, _) = await LoginAsync();
        var passport = UniquePassport();

        var first = await client.PostAsJsonAsync("/api/customers", new CreateCustomerRequest(
            "اتباع", "اول", null, "09123334477", null, null, null,
            CustomerKind.ForeignPassportOnly, passport));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/customers", new CreateCustomerRequest(
            "اتباع", "دوم", null, "09123334488", null, null, null,
            CustomerKind.ForeignPassportOnly, passport));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("پاسپورت", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Passport_only_without_a_passport_is_refused()
    {
        var (client, _) = await LoginAsync();

        var response = await client.PostAsJsonAsync("/api/customers", new CreateCustomerRequest(
            "اتباع", "بی‌شناسه", null, "09123334499", null, null, null,
            CustomerKind.ForeignPassportOnly));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("پاسپورت", await response.Content.ReadAsStringAsync());
    }
}
