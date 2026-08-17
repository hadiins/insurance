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

namespace Aqsat.UnitTests.Schedule;

/// <summary>
/// End-to-end over real HTTP (same pattern as AuthenticationTests) — proves the schedule endpoint
/// is actually wired to InstallmentAmountCalculator/DueDateCalculator, RLS, and the Policy.Write
/// permission, not just that the calculators themselves are correct in isolation.
/// </summary>
[Collection("WebApplicationFactory")]
public class PolicyScheduleEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PolicyScheduleEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Scheduling_a_policy_creates_installments_matching_the_specs_worked_example()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        var policy = new Policy
        {
            AgencyId = fixture.AgencyAId,
            PolicyNumber = $"POL-{Guid.NewGuid():N}"[..16],
            CustomerId = Guid.Empty,
            VehicleId = Guid.Empty,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = new DateOnly(2026, 1, 1),
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2027, 1, 1),
            TotalPremium = 10_700_000m,
            DownPayment = 0,
            InstallmentCount = 0,
        };

        AgencyContext.Current = fixture.AgencyAId;
        var customer = new Customer { AgencyId = fixture.AgencyAId, ExternalCode = "EXT-1", FullName = "مشتری آزمایشی" };
        var vehicle = new Vehicle { AgencyId = fixture.AgencyAId, Plate = "11الف111" };
        seedContext.Customers.Add(customer);
        seedContext.Vehicles.Add(vehicle);
        await seedContext.SaveChangesAsync();
        policy.CustomerId = customer.Id;
        policy.VehicleId = vehicle.Id;
        seedContext.Policies.Add(policy);
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/policies/{policy.Id}/schedule", new ScheduleRequest(1_700_000m, 9));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ScheduleResultDto>();
        Assert.NotNull(result);
        Assert.False(result!.ExceedsMaxInstallments);
        Assert.Equal(9, result.Installments.Count);
        Assert.All(result.Installments, i => Assert.Equal(1_000_000m, i.Amount));
        Assert.Equal(new DateOnly(2026, 2, 1), result.Installments[0].DueDate);
        Assert.Equal(new DateOnly(2026, 10, 1), result.Installments[8].DueDate);

        AgencyContext.Current = fixture.AgencyAId;
        var persistedCount = await seedContext.Installments.AsNoTracking().CountAsync(i => i.PolicyId == policy.Id);
        Assert.Equal(9, persistedCount);
    }
}
