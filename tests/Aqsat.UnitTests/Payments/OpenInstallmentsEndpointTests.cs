using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Payments;

/// <summary>The standalone "ثبت پرداخت مستقل" flow: search a customer, then list only what they
/// still owe (Unpaid/Partial), never a settled installment, oldest due date first — so the agent
/// can pick the right one without going through the countdown dashboard first.</summary>
[Collection("WebApplicationFactory")]
public class OpenInstallmentsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OpenInstallmentsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Only_unsettled_installments_are_returned_ordered_by_due_date()
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
            $"POL-OPEN-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری پرداخت مستقل", null, null,
            Vehicle: new VehicleInput("۹۹ح۹۹۹", null, null, null, null, null), Property: null,
            today, today, today.AddYears(1), 12_000_000m, 0m, null, null, false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var scheduleResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(0m, 3));
        scheduleResponse.EnsureSuccessStatusCode();
        var schedule = await scheduleResponse.Content.ReadFromJsonAsync<ScheduleResultDto>();

        AgencyContext.Current = fixture.AgencyAId;
        var installmentIds = await seedContext.Installments
            .Where(i => i.PolicyId == policy.PolicyId).OrderBy(i => i.SeqNo).Select(i => i.Id).ToListAsync();

        // Settle the first installment in full — it must disappear from the open list.
        var due = schedule!.Installments.OrderBy(i => i.SeqNo).ToList();
        var payResponse = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installmentIds[0], due[0].Amount, due[0].DueDate, "Cash", null));
        payResponse.EnsureSuccessStatusCode();

        var custResponse = await client.GetAsync($"/api/customers?search={Uri.EscapeDataString("مشتری پرداخت مستقل")}");
        custResponse.EnsureSuccessStatusCode();
        var customers = await custResponse.Content.ReadFromJsonAsync<List<CustomerListItemDto>>();
        var customerId = customers!.Single().Id;

        var openResponse = await client.GetFromJsonAsync<List<OpenInstallmentDto>>($"/api/customers/{customerId}/open-installments");

        Assert.Equal(2, openResponse!.Count);
        Assert.DoesNotContain(openResponse, i => i.InstallmentId == installmentIds[0]);
        Assert.True(openResponse[0].DueDate <= openResponse[1].DueDate);
    }
}
