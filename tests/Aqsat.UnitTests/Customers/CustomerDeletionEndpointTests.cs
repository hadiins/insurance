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

namespace Aqsat.UnitTests.Customers;

[Collection("WebApplicationFactory")]
public class CustomerDeletionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public CustomerDeletionEndpointTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

    [Fact]
    public async Task A_completely_historyless_customer_is_physically_deleted_and_audited()
    {
        await using var seed = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seed);
        AgencyContext.Current = fixture.AgencyAId;
        var customer = NewCustomer(fixture.AgencyAId, "مشتری قابل حذف");
        seed.Customers.Add(customer);
        await seed.SaveChangesAsync();
        var client = await LoginAsync(fixture);

        var row = FindListed(await client.GetFromJsonAsync<List<CustomerListItemDto>>("/api/customers"), customer.Id);
        Assert.True(row.CanDelete);
        Assert.Equal(0, row.PolicyCount);

        var response = await client.DeleteAsync($"/api/customers/{customer.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        Assert.False(await verify.Customers.IgnoreQueryFilters().AnyAsync(c => c.Id == customer.Id));
        Assert.True(await verify.AuditEntries.AnyAsync(a =>
            a.EntityType == nameof(Customer) && a.EntityId == customer.Id && a.Action == AuditAction.Deleted));
    }

    [Fact]
    public async Task A_customer_with_a_policy_is_not_deletable_even_after_a_direct_delete_call()
    {
        await using var seed = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seed);
        await InsuranceLineSeeder.EnsureSeededAsync(seed);
        AgencyContext.Current = fixture.AgencyAId;
        var lineId = await seed.InsuranceLines.Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode)
            .Select(l => l.Id).SingleAsync();
        var customer = NewCustomer(fixture.AgencyAId, "مشتری دارای بیمه نامه");
        var vehicle = new Vehicle { AgencyId = fixture.AgencyAId, Plate = "11ب111" };
        seed.Customers.Add(customer);
        seed.Vehicles.Add(vehicle);
        await seed.SaveChangesAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        seed.Policies.Add(new Policy
        {
            AgencyId = fixture.AgencyAId, PolicyNumber = $"POL-DEL-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = lineId, CustomerId = customer.Id, VehicleId = vehicle.Id,
            ContractName = "تست حذف", IsInstallment = false, Status = PolicyStatus.Active,
            IssueDate = today, StartDate = today, EndDate = today.AddYears(1), NetPremium = 1_000_000m,
        });
        await seed.SaveChangesAsync();
        var client = await LoginAsync(fixture);

        var row = FindListed(await client.GetFromJsonAsync<List<CustomerListItemDto>>("/api/customers"), customer.Id);
        Assert.False(row.CanDelete);
        Assert.Equal(1, row.PolicyCount);

        var response = await client.DeleteAsync($"/api/customers/{customer.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        Assert.True(await verify.Customers.AnyAsync(c => c.Id == customer.Id));
    }

    [Fact]
    public async Task Soft_deleted_related_history_still_blocks_physical_deletion()
    {
        await using var seed = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seed);
        AgencyContext.Current = fixture.AgencyAId;
        var customer = NewCustomer(fixture.AgencyAId, "مشتری با سابقه پنهان");
        seed.Customers.Add(customer);
        await seed.SaveChangesAsync();
        seed.CreditReports.Add(new CreditReport
        {
            AgencyId = fixture.AgencyAId, CustomerId = customer.Id, RawSuccess = false,
            RetrievedAtUtc = DateTimeOffset.UtcNow, IsDeleted = true, DeletedAt = DateTimeOffset.UtcNow,
        });
        await seed.SaveChangesAsync();
        var client = await LoginAsync(fixture);

        var row = FindListed(await client.GetFromJsonAsync<List<CustomerListItemDto>>("/api/customers"), customer.Id);
        Assert.False(row.CanDelete);
        var response = await client.DeleteAsync($"/api/customers/{customer.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static CustomerListItemDto FindListed(List<CustomerListItemDto>? rows, Guid id)
    {
        Assert.NotNull(rows);
        return Assert.Single(rows.Where(x => x.Id == id));
    }

    private static Customer NewCustomer(Guid agencyId, string name) => new()
    {
        AgencyId = agencyId, ExternalCode = $"EXT-{Guid.NewGuid():N}"[..20], FullName = name,
        FirstName = "ساخت", LastName = "آزمایشی", Mobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}",
    };

    private async Task<HttpClient> LoginAsync(DevSeeder.SeededAuthFixture fixture)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await response.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());
        return client;
    }
}
