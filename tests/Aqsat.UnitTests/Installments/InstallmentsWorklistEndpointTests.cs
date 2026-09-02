using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Installments;

/// <summary>اقساط معوق / تسویه‌های جزئی — no date window, unlike /api/countdown.</summary>
[Collection("WebApplicationFactory")]
public class InstallmentsWorklistEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public InstallmentsWorklistEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task OverdueOnly_excludes_installments_whose_deadline_has_not_passed()
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
        var uniqueTag = Guid.NewGuid().ToString("N")[..8];

        // Issue date far in the past so the first monthly installment's deadline is already gone.
        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-WL-{uniqueTag}", lineId, null, $"مشتری کارتابل {uniqueTag}", null, null,
            Vehicle: new VehicleInput("۵۵د۶۶۶", null, null, null, null, null), Property: null,
            today.AddMonths(-3), today.AddMonths(-3), today.AddYears(1), 9_000_000m, 0m, null, null, false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var scheduleResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(0m, 3));
        scheduleResponse.EnsureSuccessStatusCode();

        var overdueOnly = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>("/api/installments?overdueOnly=true");
        Assert.Contains(overdueOnly!, i => i.PolicyId == policy.PolicyId);
        Assert.All(overdueOnly!.Where(i => i.PolicyId == policy.PolicyId), i => Assert.Equal("Overdue", i.Urgency));

        var all = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>("/api/installments");
        Assert.Equal(3, all!.Count(i => i.PolicyId == policy.PolicyId));
    }

    [Fact]
    public async Task FromTo_filters_and_search_by_mobile_national_id_and_plate_find_the_policy()
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
        var mobile = $"0913{uniqueTag}1"[..11];
        var nationalId = MakeValidNationalId($"007{uniqueTag}45"[..9]);

        // Structured plate — PlateNormalized is only derived from the four structured parts, so a
        // free-text plate would leave nothing to search on (PoliciesController §5.3).
        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-WL-{uniqueTag}", lineId, null, $"مشتری جستجو {uniqueTag}", mobile, nationalId,
            Vehicle: new VehicleInput(null, null, null, null, null, null,
                PlateTwoDigit: "12", PlateLetter: "ب", PlateThreeDigit: "345", PlateIranCode: "11"),
            Property: null,
            today.AddMonths(-3), today.AddMonths(-3), today.AddYears(1), 9_000_000m, 0m, null, null, false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var scheduleResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(0m, 3));
        scheduleResponse.EnsureSuccessStatusCode();

        // Due dates follow the Jalali calendar (DueDateCalculator), which can sit a day or two off
        // Gregorian AddMonths — so the window is derived from the actual due dates, not assumed.
        var byPolicy = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>(
            $"/api/installments?search={uniqueTag}");
        var policyRows = byPolicy!.Where(i => i.PolicyId == policy.PolicyId).ToList();
        Assert.Equal(3, policyRows.Count);

        // Rows come back by due date, and the customer's mobile is on every row.
        Assert.Equal(policyRows.Select(i => i.DueDate).OrderBy(d => d), policyRows.Select(i => i.DueDate));
        Assert.All(policyRows, i => Assert.Equal(mobile, i.CustomerMobile));

        // A window around only the first due date keeps exactly the first installment.
        var firstDue = policyRows.Min(i => i.DueDate);
        var windowed = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>(
            $"/api/installments?from={firstDue.AddDays(-1):O}&to={firstDue.AddDays(1):O}");
        var row = Assert.Single(windowed!.Where(i => i.PolicyId == policy.PolicyId).ToList());
        Assert.Equal(1, row.SeqNo);

        // Each identity key finds the same rows: mobile, national ID, and plate (typed with
        // Persian digits to prove the DigitNormalizer path).
        foreach (var term in (string[])[mobile, nationalId, "۱۲ب۳۴۵"])
        {
            var found = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>(
                $"/api/installments?search={Uri.EscapeDataString(term)}");
            Assert.Contains(found!, i => i.PolicyId == policy.PolicyId);
        }
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
