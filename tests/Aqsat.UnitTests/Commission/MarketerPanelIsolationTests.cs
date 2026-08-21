using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Security;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Commission;

/// <summary>
/// Task 13's own check: a marketer sees their own customers and nothing else. Two marketers, each
/// with their own login and their own customer, in the same agency — marketer A's panel must never
/// surface marketer B's customer.
/// </summary>
[Collection("WebApplicationFactory")]
public class MarketerPanelIsolationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MarketerPanelIsolationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task A_marketers_panel_never_shows_another_marketers_customer()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        AgencyContext.Current = fixture.AgencyAId;

        var salisLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var marketerOnlyRole = new Role { Name = $"MarketerOnly-{Guid.NewGuid():N}"[..24] };
        seedContext.Roles.Add(marketerOnlyRole);
        await seedContext.SaveChangesAsync();
        seedContext.RolePermissions.Add(new RolePermission { RoleId = marketerOnlyRole.Id, Permission = Permissions.MarketerSelfView });
        await seedContext.SaveChangesAsync();

        var passwordHash = new PasswordHasher().Hash(DevSeeder.SeededUserPassword);

        var (marketerAUser, marketerA) = await SeedMarketerLoginAsync(seedContext, fixture.AgencyAId, marketerOnlyRole.Id, passwordHash, "بازاریاب الف");
        var (_, marketerB) = await SeedMarketerLoginAsync(seedContext, fixture.AgencyAId, marketerOnlyRole.Id, passwordHash, "بازاریاب ب");

        await SeedCustomerForMarketerAsync(seedContext, fixture.AgencyAId, salisLineId, marketerA.Id, "مشتری الف");
        await SeedCustomerForMarketerAsync(seedContext, fixture.AgencyAId, salisLineId, marketerB.Id, "مشتری ب");

        var clientA = _factory.CreateClient();
        var loginA = await clientA.PostAsJsonAsync("/api/auth/login", new LoginRequest(marketerAUser.Mobile, DevSeeder.SeededUserPassword));
        loginA.EnsureSuccessStatusCode();
        var tokenA = (await loginA.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        clientA.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        var customersForA = await clientA.GetFromJsonAsync<List<MarketerCustomerDto>>("/api/marketer-panel/customers");

        Assert.NotNull(customersForA);
        Assert.Single(customersForA!);
        Assert.Equal("مشتری الف", customersForA![0].FullName);
    }

    private static async Task<(AppUser User, Marketer Marketer)> SeedMarketerLoginAsync(
        AppDbContext context, Guid agencyId, Guid roleId, string passwordHash, string fullName)
    {
        var mobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}";
        var user = new AppUser { FullName = fullName, Mobile = mobile, PasswordHash = passwordHash, IsActive = true };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.UserOrgRoles.Add(new UserOrgRole { UserId = user.Id, OrganizationId = agencyId, RoleId = roleId });
        await context.SaveChangesAsync();

        var marketer = new Marketer { AgencyId = agencyId, FullName = fullName, Mobile = mobile, Type = MarketerType.Independent, AppUserId = user.Id, IsActive = true };
        context.Marketers.Add(marketer);
        await context.SaveChangesAsync();

        return (user, marketer);
    }

    private static async Task SeedCustomerForMarketerAsync(
        AppDbContext context, Guid agencyId, Guid insuranceLineId, Guid marketerId, string customerName)
    {
        var customer = new Customer { AgencyId = agencyId, ExternalCode = $"EXT-{Guid.NewGuid():N}"[..12], FullName = customerName };
        var vehicle = new Vehicle { AgencyId = agencyId, Plate = "77ز777" };
        context.Customers.Add(customer);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var policy = new Policy
        {
            AgencyId = agencyId,
            PolicyNumber = $"POL-ISO-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = insuranceLineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = today,
            StartDate = today,
            EndDate = today.AddYears(1),
            NetPremium = 5_000_000m,
            DownPayment = 0,
            MarketerId = marketerId,
        };
        context.Policies.Add(policy);
        await context.SaveChangesAsync();
    }
}
