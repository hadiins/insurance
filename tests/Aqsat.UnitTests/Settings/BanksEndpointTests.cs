using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Aqsat.UnitTests.Settings;

/// <summary>فهرست بانک‌ها — the agency-editable list of bank names backing the cheque forms'
/// bank dropdown. First read lazily seeds the standard Iranian banks; after that it is plain
/// CRUD with per-agency isolation (a second agency's list must be invisible and its own seed
/// must not see the first agency's rows as duplicates).</summary>
[Collection("WebApplicationFactory")]
public class BanksEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BanksEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task First_read_seeds_the_default_list_then_crud_and_isolation_apply()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        var client = await LoginClientAsync(fixture.DualAgencyManagerMobile, fixture.AgencyAId);

        var seeded = await client.GetFromJsonAsync<List<BankDto>>("/api/settings/cash-and-bank/banks");
        Assert.NotEmpty(seeded!);
        Assert.Contains(seeded!, b => b.Name == "بانک ملی ایران" && b.IsActive);
        Assert.All(seeded!, b => Assert.True(b.IsActive));

        // Second read is stable — the seed runs once, never duplicated.
        var again = await client.GetFromJsonAsync<List<BankDto>>("/api/settings/cash-and-bank/banks");
        Assert.Equal(seeded!.Count, again!.Count);

        var create = await client.PostAsJsonAsync(
            "/api/settings/cash-and-bank/banks", new CreateBankRequest("بانک تستی اقساط"));
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<BankDto>();
        Assert.Equal("بانک تستی اقساط", created!.Name);

        var duplicate = await client.PostAsJsonAsync(
            "/api/settings/cash-and-bank/banks", new CreateBankRequest("بانک تستی اقساط"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var update = await client.PutAsJsonAsync(
            $"/api/settings/cash-and-bank/banks/{created.Id}", new UpdateBankRequest("بانک تستی (غیرفعال)", false));
        update.EnsureSuccessStatusCode();
        var updated = await update.Content.ReadFromJsonAsync<BankDto>();
        Assert.False(updated!.IsActive);

        var remove = await client.DeleteAsync($"/api/settings/cash-and-bank/banks/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        var afterDelete = await client.GetFromJsonAsync<List<BankDto>>("/api/settings/cash-and-bank/banks");
        Assert.DoesNotContain(afterDelete!, b => b.Id == created.Id);

        // RLS: agency B's first read seeds its OWN list; A's custom bank never leaks across.
        var clientB = await LoginClientAsync(fixture.DualAgencyManagerMobile, fixture.AgencyBId);
        var agencyB = await clientB.GetFromJsonAsync<List<BankDto>>("/api/settings/cash-and-bank/banks");
        Assert.DoesNotContain(agencyB!, b => b.Name == "بانک تستی (غیرفعال)");
        Assert.Equal(seeded!.Count, agencyB!.Count); // B got its own full default seed, nothing more
    }

    private async Task<HttpClient> LoginClientAsync(string mobile, Guid organizationId)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, DevSeeder.SeededUserPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", organizationId.ToString());
        return client;
    }
}
