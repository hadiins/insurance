using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Reports;

/// <summary>docs/TASK-24-POLICY-NUMBER.md §3 — a free Fanavaran reconciliation tool: every serial
/// gap in the agency's sequence for a given year.</summary>
[Collection("WebApplicationFactory")]
public class MissingSerialsReportEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MissingSerialsReportEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Finds_the_gaps_between_registered_serials()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var lineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        AgencyContext.Current = fixture.AgencyAId;
        var customer = new Customer { AgencyId = fixture.AgencyAId, ExternalCode = "EXT-MS", FullName = "مشتری" };
        seedContext.Customers.Add(customer);
        await seedContext.SaveChangesAsync();

        // Serials 1, 3, 5 registered for year 1405 — 2 and 4 are the gaps.
        foreach (var serial in new[] { "000001", "000003", "000005" })
        {
            seedContext.Policies.Add(new Policy
            {
                AgencyId = fixture.AgencyAId,
                PolicyNumber = $"1110/576210/405/{serial}",
                InsuranceLineId = lineId,
                CustomerId = customer.Id,
                ContractName = "تست",
                IsInstallment = true,
                IssueDate = new DateOnly(2026, 4, 1),
                StartDate = new DateOnly(2026, 4, 1),
                EndDate = new DateOnly(2027, 4, 1),
                NetPremium = 9_000_000m,
                PnIsParsed = true,
                PnYear = 1405,
                PnSerial = serial,
            });
        }
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        var report = await client.GetFromJsonAsync<MissingSerialsReportDto>("/api/reports/missing-serials?year=1405");

        Assert.NotNull(report);
        Assert.Equal(3, report!.RegisteredCount);
        Assert.Equal(2, report.MissingCount);
        Assert.Equal(["000002", "000004"], report.Missing);
        Assert.Equal("000005", report.RangeEnd);
    }

    [Fact]
    public async Task A_year_with_no_registered_policies_reports_zero_missing()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        var report = await client.GetFromJsonAsync<MissingSerialsReportDto>("/api/reports/missing-serials?year=1399");

        Assert.NotNull(report);
        Assert.Equal(0, report!.RegisteredCount);
        Assert.Equal(0, report.MissingCount);
        Assert.Null(report.RangeEnd);
    }
}
