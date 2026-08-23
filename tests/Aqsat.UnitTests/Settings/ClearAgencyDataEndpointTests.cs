using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Domain;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Settings;

/// <summary>«منطقهٔ خطر» — clears every policy/installment for the caller's own agency, gated
/// behind typing the agency's own Code back.</summary>
[Collection("WebApplicationFactory")]
public class ClearAgencyDataEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ClearAgencyDataEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Wrong_confirm_code_is_rejected_and_correct_code_clears_installments_and_policies()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(seedContext);

        var role = new Role { Name = $"Manager-{Guid.NewGuid():N}"[..16] };
        seedContext.Roles.Add(role);
        await seedContext.SaveChangesAsync();
        foreach (var permission in Permissions.All.Where(p => p != Permissions.PlatformOwner))
        {
            seedContext.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
        }
        await seedContext.SaveChangesAsync();

        var hasher = new Aqsat.Infrastructure.Security.PasswordHasher();
        var mobile = $"0919{Guid.NewGuid():N}"[..11];
        var user = new AppUser { FullName = "مدیر تستی", Mobile = mobile, PasswordHash = hasher.Hash(DevSeeder.SeededUserPassword), IsActive = true };
        seedContext.Users.Add(user);
        await seedContext.SaveChangesAsync();
        seedContext.UserOrgRoles.Add(new UserOrgRole { UserId = user.Id, OrganizationId = agencyA.AgencyId, RoleId = role.Id });
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", agencyA.AgencyId.ToString());

        var policiesBefore = await client.GetFromJsonAsync<List<PolicyListItemDto>>("/api/policies?");
        Assert.NotEmpty(policiesBefore!);

        var wrongCode = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/settings/agency/data")
        {
            Content = JsonContent.Create(new ClearAgencyDataRequest("wrong-code")),
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCode.StatusCode);

        var agencySettings = await client.GetFromJsonAsync<AgencySettingsDto>("/api/settings/agency");
        var correctCode = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/settings/agency/data")
        {
            Content = JsonContent.Create(new ClearAgencyDataRequest(agencySettings!.Code)),
        });
        Assert.Equal(HttpStatusCode.NoContent, correctCode.StatusCode);

        var policiesAfter = await client.GetFromJsonAsync<List<PolicyListItemDto>>("/api/policies?");
        Assert.Empty(policiesAfter!);
    }
}
