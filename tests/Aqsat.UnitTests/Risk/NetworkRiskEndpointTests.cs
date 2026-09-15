using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.Infrastructure.Security;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aqsat.UnitTests.Risk;

/// <summary>
/// Phase 2B-1 — the cross-agency network lookup: default-OFF switch, the owner's enable flow,
/// the status-only result shape (no amounts, no identity — asserted on the raw JSON, not just the
/// typed DTO), plate search across two owners, upsert on re-assessment, the per-lookup audit row,
/// and the settings endpoint's Platform.Owner gate.
/// </summary>
[Collection("WebApplicationFactory")]
public class NetworkRiskEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public NetworkRiskEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private static IFieldEncryptor BuildFieldEncryptor()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Same key as appsettings.Development.json — the test host runs in Development, so
                // hashes computed here match what the API's own encryptor produces.
                ["Encryption:NationalIdKey"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
            })
            .Build();
        return new AesFieldEncryptor(configuration);
    }

    private async Task<HttpClient> LoginClientAsync(DevSeeder.SeededAuthFixture fixture, Guid orgId)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());
        return client;
    }

    private static string UniqueNationalId()
    {
        var digits = $"009{Random.Shared.Next(1_000_000):D6}";
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (digits[i] - '0') * (10 - i);
        }
        var remainder = sum % 11;
        return digits + (remainder < 2 ? remainder : 11 - remainder);
    }

    /// <summary>A random plate in the parser's normalized form ("12د345-67") — the shared test
    /// database persists between tests, so a fixed plate would collide across fixtures.</summary>
    private static string UniquePlate() =>
        $"{Random.Shared.Next(10, 99)}د{Random.Shared.Next(100, 999)}-{Random.Shared.Next(10, 99)}";

    /// <summary>A customer with one open overdue installment — assessing them produces a real
    /// (non-insufficient-data) assessment, which is what writes the NetworkRiskProfile row.</summary>
    private static async Task<(Guid CustomerId, string NationalId)> SeedAssessedCustomerAsync(Guid agencyId)
    {
        await using var context = TestDbContextFactory.Create();
        await InsuranceLineSeeder.EnsureSeededAsync(context);
        var lineId = await context.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode)
            .Select(l => l.Id)
            .FirstAsync();

        AgencyContext.Current = agencyId;
        var nationalId = UniqueNationalId();
        var customer = new Customer
        {
            AgencyId = agencyId,
            ExternalCode = $"N2-{Guid.NewGuid():N}"[..16],
            FullName = "مشتری شبکه",
            NationalId = nationalId,
            Mobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}",
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var policy = new Policy
        {
            AgencyId = agencyId,
            PolicyNumber = $"N2-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = lineId,
            CustomerId = customer.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3)),
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(9)),
            NetPremium = 120_000_000,
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
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
            SettlementDeadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)),
            Amount = 30_000_000,
            Status = InstallmentStatus.Unpaid,
        });
        await context.SaveChangesAsync();

        return (customer.Id, nationalId);
    }

    /// <summary>Gives the dual-agency manager a Platform.Owner role at the seeded HQ so the test
    /// can drive the settings endpoints exactly the way the real owner would.</summary>
    private static async Task GrantPlatformOwnerAsync(DevSeeder.SeededAuthFixture fixture)
    {
        await using var context = TestDbContextFactory.Create();
        var ownerRole = new Role { Name = $"Owner-{Guid.NewGuid():N}"[..20] };
        context.Roles.Add(ownerRole);
        await context.SaveChangesAsync();
        context.RolePermissions.Add(new RolePermission { RoleId = ownerRole.Id, Permission = Permissions.PlatformOwner });
        context.UserOrgRoles.Add(new UserOrgRole
        {
            UserId = fixture.DualAgencyManagerId,
            OrganizationId = fixture.HeadquartersId,
            RoleId = ownerRole.Id,
        });
        await context.SaveChangesAsync();
    }

    /// <summary>Sets the singleton switch directly — the shared test database persists between
    /// tests, so every test must pin the state it assumes instead of relying on insert order.</summary>
    private static async Task SetNetworkEnabledAsync(bool enabled)
    {
        await using var context = TestDbContextFactory.Create();
        var settings = await context.RiskNetworkSettings.SingleOrDefaultAsync();
        if (settings is null)
        {
            context.RiskNetworkSettings.Add(new RiskNetworkSettings
            {
                IsEnabled = enabled,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            settings.IsEnabled = enabled;
        }

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task The_switch_is_off_by_default_even_with_data_present()
    {
        await using var seedContext = TestDbContextFactory.Create();
        // The settings singleton is PLATFORM-wide on the shared test database — a test that
        // enabled it earlier in this run would otherwise leak ON state in here. "Default" can
        // only be asserted after clearing the row (soft-deleted rows are invisible to the
        // service's filtered read).
        await seedContext.RiskNetworkSettings.IgnoreQueryFilters().ExecuteDeleteAsync();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await SetNetworkEnabledAsync(false);
        var (customerId, nationalId) = await SeedAssessedCustomerAsync(fixture.AgencyAId);

        var clientA = await LoginClientAsync(fixture, fixture.AgencyAId);
        var assess = await clientA.PostAsJsonAsync($"/api/customers/{customerId}/risk/assess", new { });
        assess.EnsureSuccessStatusCode();

        AgencyContext.Current = fixture.AgencyAId;
        await using var verify = TestDbContextFactory.Create();
        Assert.True(await verify.NetworkRiskProfiles.AsNoTracking()
            .AnyAsync(p => p.AgencyId == fixture.AgencyAId && p.CustomerId == customerId));

        var clientB = await LoginClientAsync(fixture, fixture.AgencyBId);
        var result = await clientB.GetFromJsonAsync<NetworkRiskLookupApiDto>(
            $"/api/risk/network?nationalId={nationalId}");
        Assert.NotNull(result);
        Assert.False(result!.IsEnabled);
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task Once_the_owner_enables_it_agency_B_sees_A_status_and_the_shape_has_no_amounts_or_identity()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await GrantPlatformOwnerAsync(fixture);
        var (customerId, nationalId) = await SeedAssessedCustomerAsync(fixture.AgencyAId);

        var owner = await LoginClientAsync(fixture, fixture.HeadquartersId);
        var put = await owner.PutAsJsonAsync("/api/risk/network/settings", new UpdateRiskNetworkSettingsRequest(true));
        put.EnsureSuccessStatusCode();

        var clientA = await LoginClientAsync(fixture, fixture.AgencyAId);
        var assess = await clientA.PostAsJsonAsync($"/api/customers/{customerId}/risk/assess", new { });
        assess.EnsureSuccessStatusCode();

        var clientB = await LoginClientAsync(fixture, fixture.AgencyBId);
        using var raw = await clientB.GetAsync($"/api/risk/network?nationalId={nationalId}");
        raw.EnsureSuccessStatusCode();
        var json = await raw.Content.ReadAsStringAsync();

        var result = await clientB.GetFromJsonAsync<NetworkRiskLookupApiDto>(
            $"/api/risk/network?nationalId={nationalId}");
        Assert.True(result!.IsEnabled);
        var row = Assert.Single(result.Results);
        Assert.Equal("نمایندگی ۴۰۰۱", row.AgencyName);
        Assert.False(row.IsOwnAgency);
        // Identity never leaks through the shared row: the raw JSON carries no customer/person fields.
        Assert.DoesNotContain("customerId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Toman", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fullName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mobile", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("score", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overdueCount", json, StringComparison.OrdinalIgnoreCase);

        // A's own view of the same person carries the «دفتر خودتان» marker.
        var own = await clientA.GetFromJsonAsync<NetworkRiskLookupApiDto>(
            $"/api/risk/network?nationalId={nationalId}");
        Assert.True(own!.Results.Single().IsOwnAgency);

        // The lookup itself was audited under B's agency — the one compliance requirement (rule 29).
        AgencyContext.Current = fixture.AgencyBId;
        await using var verify = TestDbContextFactory.Create();
        Assert.True(await verify.AuditEntries.AsNoTracking().AnyAsync(a =>
            a.AgencyId == fixture.AgencyBId
            && a.EntityType == "NetworkRiskLookup"
            && a.Action == AuditAction.NetworkRiskLookup
            && a.Description.Contains(nationalId)));
    }

    [Fact]
    public async Task A_plate_lookup_returns_every_agency_that_ever_owned_the_plate()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await GrantPlatformOwnerAsync(fixture);
        var (customerA, nationalIdA) = await SeedAssessedCustomerAsync(fixture.AgencyAId);
        var (customerB, nationalIdB) = await SeedAssessedCustomerAsync(fixture.AgencyBId);

        var owner = await LoginClientAsync(fixture, fixture.HeadquartersId);
        (await owner.PutAsJsonAsync("/api/risk/network/settings", new UpdateRiskNetworkSettingsRequest(true)))
            .EnsureSuccessStatusCode();

        var clientA = await LoginClientAsync(fixture, fixture.AgencyAId);
        (await clientA.PostAsJsonAsync($"/api/customers/{customerA}/risk/assess", new { })).EnsureSuccessStatusCode();
        var clientB = await LoginClientAsync(fixture, fixture.AgencyBId);
        (await clientB.PostAsJsonAsync($"/api/customers/{customerB}/risk/assess", new { })).EnsureSuccessStatusCode();

        // The plate index rows the nightly sync would have written for this pair.
        var plate = UniquePlate();
        var encryptor = BuildFieldEncryptor();
        await using var indexContext = TestDbContextFactory.Create();
        indexContext.NetworkRiskPlateIndex.AddRange(
            new NetworkRiskPlateIndex
            {
                Id = SequentialGuidGenerator.Next(),
                AgencyId = fixture.AgencyAId,
                PlateNormalized = plate,
                NationalIdHash = encryptor.Hash(nationalIdA),
                SyncedAt = DateTimeOffset.UtcNow,
            },
            new NetworkRiskPlateIndex
            {
                Id = SequentialGuidGenerator.Next(),
                AgencyId = fixture.AgencyBId,
                PlateNormalized = plate,
                NationalIdHash = encryptor.Hash(nationalIdB),
                SyncedAt = DateTimeOffset.UtcNow,
            });
        await indexContext.SaveChangesAsync();

        // Free-form Persian input — the server normalizes via PlateParser before matching. The raw
        // digits match UniquePlate's but in display order (two letter three ایران iran).
        var display = $"{plate[..2]} {plate[2..3]} {plate[3..6]} ایران {plate[7..]}";
        var result = await clientB.GetFromJsonAsync<NetworkRiskLookupApiDto>(
            $"/api/risk/network?plate={Uri.EscapeDataString(display)}");
        Assert.True(result!.IsEnabled);
        Assert.Equal(2, result.Results.Count);
        Assert.Contains(result.Results, r => r.IsOwnAgency);
        Assert.Contains(result.Results, r => !r.IsOwnAgency);
    }

    [Fact]
    public async Task Re_assessment_updates_the_single_network_row_instead_of_duplicating()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await GrantPlatformOwnerAsync(fixture);
        var (customerId, nationalId) = await SeedAssessedCustomerAsync(fixture.AgencyAId);

        var clientA = await LoginClientAsync(fixture, fixture.AgencyAId);
        (await clientA.PostAsJsonAsync($"/api/customers/{customerId}/risk/assess", new { })).EnsureSuccessStatusCode();

        // Settle the overdue installment, then re-assess: the same person, healthier numbers.
        AgencyContext.Current = fixture.AgencyAId;
        await using var settleContext = TestDbContextFactory.Create();
        var installment = await settleContext.Installments
            .Where(i => i.Policy.CustomerId == customerId)
            .FirstAsync();
        installment.Status = InstallmentStatus.Settled;
        installment.PaidAmount = installment.Amount;
        await settleContext.SaveChangesAsync();

        (await clientA.PostAsJsonAsync($"/api/customers/{customerId}/risk/assess", new { })).EnsureSuccessStatusCode();

        var encryptor = BuildFieldEncryptor();
        var hash = encryptor.Hash(nationalId);
        var rows = await settleContext.NetworkRiskProfiles.AsNoTracking()
            .Where(p => p.AgencyId == fixture.AgencyAId && p.NationalIdHash == hash)
            .ToListAsync();
        var row = Assert.Single(rows);

        var latest = await settleContext.RiskAssessments.AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.CalculatedAt)
            .FirstAsync();
        Assert.Equal(latest.Score, row.Score);
        Assert.Equal(0, row.OverdueCount);
    }

    [Fact]
    public async Task Settings_are_owner_only_and_the_network_requires_authentication()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        var clientB = await LoginClientAsync(fixture, fixture.AgencyBId);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientB.GetAsync("/api/risk/network/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await clientB.PutAsJsonAsync("/api/risk/network/settings", new UpdateRiskNetworkSettingsRequest(true))).StatusCode);

        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/risk/network?nationalId=0099999999")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/risk/network/settings")).StatusCode);
    }
}
