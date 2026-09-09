using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Customers;

/// <summary>
/// POST /api/customers — registering a brand-new customer BEFORE any policy exists, so a
/// pre-issuance credit-check portal link can be sent on the very first visit (owner decision
/// 2026-09-03). Same validation rules the issuance wizard's inline registration has always
/// enforced (now shared through CustomerCreationService), plus a national-ID duplicate guard.
/// </summary>
[Collection("WebApplicationFactory")]
public class CreateCustomerEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CreateCustomerEndpointTests(WebApplicationFactory<Program> factory)
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

    /// <summary>The WebApplicationFactory collection shares the AqsatTest database, and other
    /// classes seed fixed valid IDs like 0072345453 — so every test here mints its own
    /// checksum-valid ID to stay clear of the duplicate guard.</summary>
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

    private static CreateCustomerRequest ValidRequest(string nationalId) => new(
        "مشتری", "تازه‌وارد", nationalId, "09123334444", null, null, null);

    [Fact]
    public async Task Creating_a_customer_registers_them_and_returns_the_lookup_shape()
    {
        var (client, fixture) = await LoginAsync();
        var nationalId = UniqueNationalId();

        var response = await client.PostAsJsonAsync("/api/customers", ValidRequest(nationalId));
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<CustomerLookupResultDto>();
        Assert.True(dto!.Found);
        Assert.Equal("مشتری تازه‌وارد", dto.Customer!.FullName);
        Assert.Equal(nationalId, dto.Customer.NationalId);
        Assert.Equal("09123334444", dto.Customer.Mobile);
        Assert.Equal(0, dto.PolicyCount);
        Assert.False(dto.Customer.IsProfileComplete); // address/postal code are still missing

        // The wizard's step-1 lookup finds the new customer immediately.
        var lookup = await client.GetAsync($"/api/customers/lookup?nationalId={nationalId}");
        lookup.EnsureSuccessStatusCode();
        var found = await lookup.Content.ReadFromJsonAsync<CustomerLookupResultDto>();
        Assert.True(found!.Found);
        Assert.Equal(dto.Customer.Id, found.Customer!.Id);

        // The keyed HMAC travels alongside the plaintext column (rule 12) for dedupe/audit paths.
        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var row = await verify.Customers.AsNoTracking().FirstAsync(c => c.Id == dto.Customer.Id);
        Assert.NotNull(row.NationalIdHash);
        Assert.Equal(nationalId, row.NationalId);
    }

    [Fact]
    public async Task A_duplicate_national_id_is_refused_with_a_persian_reason()
    {
        var (client, _) = await LoginAsync();
        var nationalId = UniqueNationalId();

        var first = await client.PostAsJsonAsync("/api/customers", ValidRequest(nationalId));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/customers", ValidRequest(nationalId));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("کد ملی", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invalid_input_is_refused()
    {
        var (client, _) = await LoginAsync();

        var noNationalId = await client.PostAsJsonAsync(
            "/api/customers", ValidRequest(UniqueNationalId()) with { NationalId = null });
        Assert.Equal(HttpStatusCode.BadRequest, noNationalId.StatusCode);

        var noMobile = await client.PostAsJsonAsync(
            "/api/customers", ValidRequest(UniqueNationalId()) with { Mobile = null });
        Assert.Equal(HttpStatusCode.BadRequest, noMobile.StatusCode);

        var badNationalId = await client.PostAsJsonAsync(
            "/api/customers", ValidRequest(UniqueNationalId()) with { NationalId = "1234567890" });
        Assert.Equal(HttpStatusCode.BadRequest, badNationalId.StatusCode);

        var badMobile = await client.PostAsJsonAsync(
            "/api/customers", ValidRequest(UniqueNationalId()) with { Mobile = "not-a-mobile" });
        Assert.Equal(HttpStatusCode.BadRequest, badMobile.StatusCode);

        var sameEmergencyMobile = await client.PostAsJsonAsync(
            "/api/customers", ValidRequest(UniqueNationalId()) with { EmergencyMobile = "09123334444" });
        Assert.Equal(HttpStatusCode.BadRequest, sameEmergencyMobile.StatusCode);

        // Production incident 2026-09-07 — a national ID in the lastName field.
        var digitsOnlyLastName = await client.PostAsJsonAsync(
            "/api/customers", ValidRequest(UniqueNationalId()) with { LastName = "۰۳۸۶۵۲۹۵۵۸" });
        Assert.Equal(HttpStatusCode.BadRequest, digitsOnlyLastName.StatusCode);
    }

    [Fact]
    public async Task Another_agency_cannot_see_the_new_customer()
    {
        var (clientA, fixture) = await LoginAsync();
        var nationalId = UniqueNationalId();
        var created = await clientA.PostAsJsonAsync("/api/customers", ValidRequest(nationalId));
        created.EnsureSuccessStatusCode();

        // Operator B — the fixture's dual-agency user acting in agency B (Staff role there).
        var clientB = _factory.CreateClient();
        var loginB = await clientB.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginB.EnsureSuccessStatusCode();
        var tokenB = (await loginB.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        clientB.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyBId.ToString());

        var lookup = await clientB.GetAsync($"/api/customers/lookup?nationalId={nationalId}");
        lookup.EnsureSuccessStatusCode();
        var found = await lookup.Content.ReadFromJsonAsync<CustomerLookupResultDto>();
        Assert.False(found!.Found); // RLS: agency B's scope simply does not contain the row
        Assert.Null(found.Customer);
    }

    [Fact]
    public async Task An_unauthenticated_caller_cannot_create()
    {
        var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/customers", ValidRequest(UniqueNationalId()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
