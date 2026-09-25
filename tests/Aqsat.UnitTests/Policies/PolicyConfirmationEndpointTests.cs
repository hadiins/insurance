using Aqsat.Application.Common;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Seed;
using Aqsat.Infrastructure.Persistence;

using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Policies;

/// <summary>در انتظار تأیید مشتری — mark-pending-confirmation / confirm is a two-state internal
/// worklist flag, not a customer-facing flow.</summary>
[Collection("WebApplicationFactory")]
public class PolicyConfirmationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PolicyConfirmationEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Marking_pending_then_confirming_round_trips_through_the_list_filter()
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

        var today = IranClock.Today();
        var uniqueTag = Guid.NewGuid().ToString("N")[..8];
        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-PEND-{uniqueTag}", lineId, null, $"مشتری تأیید {uniqueTag}", null, null,
            Vehicle: new VehicleInput("۹۹و۰۰۰", null, null, null, null, null), Property: null,
            today, today, today.AddYears(1), 7_000_000m, 0m, null, null, false,
            PaymentType: "cash"));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        Assert.NotNull(policy);
        var policyId = policy!.PolicyId;

        // New manual policies already start pending; the worklist sees them immediately.
        var pendingList = await client.GetFromJsonAsync<List<PolicyListItemDto>>("/api/policies?status=PendingConfirmation");
        Assert.Contains(pendingList!, p => p.Id == policyId);

        // Confirmation cannot bypass workflow evidence.
        var prematureConfirm = await client.PutAsJsonAsync($"/api/policies/{policyId}/confirm", new { });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, prematureConfirm.StatusCode);

        AgencyContext.Current = fixture.AgencyAId;
        var cashBoxId = await seedContext.CashBoxes.AsNoTracking()
            .Where(c => c.AgencyId == fixture.AgencyAId && c.IsActive).Select(c => c.Id).FirstAsync();
        var payResponse = await client.PostAsJsonAsync($"/api/policies/{policyId}/record-full-payment",
            new RecordFullPaymentRequest(7_000_000m, today, "نقدی", null,
                MethodType: Aqsat.Domain.Enums.PaymentMethod.Cash, CashBoxId: cashBoxId));
        payResponse.EnsureSuccessStatusCode();
        var confirmResponse = await client.PostAsync($"/api/policies/{policyId}/finalize", null);
        confirmResponse.EnsureSuccessStatusCode();
        var activeList = await client.GetFromJsonAsync<List<PolicyListItemDto>>("/api/policies?status=Active");
        Assert.Contains(activeList!, p => p.Id == policyId);

        var pendingListAfter = await client.GetFromJsonAsync<List<PolicyListItemDto>>("/api/policies?status=PendingConfirmation");
        Assert.DoesNotContain(pendingListAfter!, p => p.Id == policyId);
    }
}
