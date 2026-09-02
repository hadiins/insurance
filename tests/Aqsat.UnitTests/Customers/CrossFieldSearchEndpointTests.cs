using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Customers;

/// <summary>درخواست ۵ — every search box must find a person by national ID, full name, mobile,
/// or vehicle plate. The worklist search is covered in InstallmentsWorklistEndpointTests; this
/// exercises the customers and policies lists, which the shell's main search pages call.</summary>
[Collection("WebApplicationFactory")]
public class CrossFieldSearchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CrossFieldSearchEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Customers_and_policies_find_the_same_person_by_every_identity_key()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var lineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var uniqueTag = string.Concat(Guid.NewGuid().ToString("N").Where(char.IsDigit))[..8];
        var mobile = $"0914{uniqueTag}2"[..11];
        var nationalId = MakeValidNationalId($"106{uniqueTag}77"[..9]);
        var name = $"مشتری همه‌کلیدها {uniqueTag}";

        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-XS-{uniqueTag}", lineId, null, name, mobile, nationalId,
            Vehicle: new VehicleInput(null, null, null, null, null, null,
                PlateTwoDigit: "36", PlateLetter: "د", PlateThreeDigit: "741", PlateIranCode: "84"),
            Property: null,
            today, today, today.AddYears(1), 9_000_000m, 0m, null, null, false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        // The plate typed with Persian digits proves the DigitNormalizer path end to end.
        foreach (var term in (string[])[mobile, nationalId, name, "۳۶د۷۴۱"])
        {
            var customers = await client.GetFromJsonAsync<List<CustomerListItemDto>>(
                $"/api/customers?search={Uri.EscapeDataString(term)}");
            Assert.Contains(customers!, c => c.FullName == name);

            var policies = await client.GetFromJsonAsync<List<PolicyListItemDto>>(
                $"/api/policies?search={Uri.EscapeDataString(term)}");
            Assert.Contains(policies!, p => p.Id == policy!.PolicyId);
        }

        // RLS: the same searches under agency B find nothing.
        var clientB = _factory.CreateClient();
        var loginB = await clientB.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginB.EnsureSuccessStatusCode();
        var tokenB = (await loginB.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        clientB.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyBId.ToString());

        var otherAgency = await clientB.GetFromJsonAsync<List<CustomerListItemDto>>(
            $"/api/customers?search={Uri.EscapeDataString(nationalId)}");
        Assert.DoesNotContain(otherAgency!, c => c.FullName == name);
    }

    /// <summary>NationalIdValidator rejects any made-up 10-digit string, so the search test needs a
    /// checksum-valid ID built from the same mod-11 rule the validator applies.</summary>
    private static string MakeValidNationalId(string nineDigits)
    {
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (nineDigits[i] - '0') * (10 - i);
        }
        var rem = sum % 11;
        return nineDigits + (rem < 2 ? rem : 11 - rem);
    }
}
