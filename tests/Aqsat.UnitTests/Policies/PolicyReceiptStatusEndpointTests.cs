using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Policies;

/// <summary>«ثبت دریافت» — GET /policies/{id}/receipt-status must reflect scheduling, down-payment
/// receipt, and open-installment state accurately as the policy moves through its lifecycle.</summary>
[Collection("WebApplicationFactory")]
public class PolicyReceiptStatusEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PolicyReceiptStatusEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Status_tracks_scheduling_down_payment_and_installment_settlement()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var salisLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-RS-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری وضعیت دریافت", null, null,
            Vehicle: new VehicleInput("۶۶د۶۶۶", null, null, null, null, null), Property: null,
            today, today, today.AddYears(1), 4_000_000m, 0m, null, null, false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var before = await client.GetFromJsonAsync<PolicyReceiptStatusDto>($"/api/policies/{policy!.PolicyId}/receipt-status");
        Assert.NotNull(before);
        Assert.False(before!.IsScheduled);

        var scheduleResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy.PolicyId}/schedule", new ScheduleRequest(1_000_000m, 2));
        scheduleResponse.EnsureSuccessStatusCode();

        var afterSchedule = await client.GetFromJsonAsync<PolicyReceiptStatusDto>($"/api/policies/{policy.PolicyId}/receipt-status");
        Assert.True(afterSchedule!.IsScheduled);
        Assert.False(afterSchedule.DownPaymentReceived);
        Assert.Equal(2, afterSchedule.OpenInstallments.Count);

        var receiveResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy.PolicyId}/receive-down-payment", new ReceiveDownPaymentRequest(today, null));
        receiveResponse.EnsureSuccessStatusCode();

        var afterDownPayment = await client.GetFromJsonAsync<PolicyReceiptStatusDto>($"/api/policies/{policy.PolicyId}/receipt-status");
        Assert.True(afterDownPayment!.DownPaymentReceived);

        AgencyContext.Current = fixture.AgencyAId;
        var firstInstallmentId = await seedContext.Installments
            .Where(i => i.PolicyId == policy.PolicyId).OrderBy(i => i.SeqNo).Select(i => i.Id).FirstAsync();
        var paymentResponse = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            firstInstallmentId, 1_500_000m, today, "نقدی", null));
        paymentResponse.EnsureSuccessStatusCode();

        var afterSettlement = await client.GetFromJsonAsync<PolicyReceiptStatusDto>($"/api/policies/{policy.PolicyId}/receipt-status");
        Assert.Single(afterSettlement!.OpenInstallments);
    }
}
