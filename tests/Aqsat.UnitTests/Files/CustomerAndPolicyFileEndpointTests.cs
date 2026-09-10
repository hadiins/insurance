using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Files;

/// <summary>
/// Task 17's own promise (docs/TASKS.md): customer file aggregates balance and payment history
/// across all of a customer's policies; policy file's history tab is a single indexed query on
/// AuditEntry.PolicyId, rendered as "actor — when — what".
/// </summary>
[Collection("WebApplicationFactory")]
public class CustomerAndPolicyFileEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CustomerAndPolicyFileEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Customer_file_aggregates_two_policies_and_policy_file_shows_the_payment_in_its_timeline()
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

        var today = new DateOnly(2026, 3, 10);
        const string customerName = "مشتری پروندهٔ آزمایشی";
        const string customerMobile = "09127770000";

        var firstPolicyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-FILE-{Guid.NewGuid():N}"[..16], salisLineId, null, customerName, customerMobile, null,
            Vehicle: new VehicleInput("۵۵د۵۵۵", null, null, null, null, null), Property: null,
            today, today, today.AddYears(1), 10_000_000m, 0m, null, null, false));
        firstPolicyResponse.EnsureSuccessStatusCode();
        var firstPolicy = await firstPolicyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var scheduleResponse = await client.PostAsJsonAsync(
            $"/api/policies/{firstPolicy!.PolicyId}/schedule", new ScheduleRequest(2_000_000m, 2));
        scheduleResponse.EnsureSuccessStatusCode();

        var receiveDownPaymentResponse = await client.PostAsJsonAsync(
            $"/api/policies/{firstPolicy.PolicyId}/receive-down-payment", new ReceiveDownPaymentRequest(today, null));
        receiveDownPaymentResponse.EnsureSuccessStatusCode();

        // Same customer record, explicitly reused — a second policy for the same person.
        var secondPolicyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-FILE-{Guid.NewGuid():N}"[..16], salisLineId, firstPolicy.CustomerId, null, null, null,
            Vehicle: new VehicleInput("۶۶ه۶۶۶", null, null, null, null, null), Property: null,
            today, today, today.AddYears(1), 6_000_000m, 0m, null, null, false));
        secondPolicyResponse.EnsureSuccessStatusCode();
        var secondPolicy = await secondPolicyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();
        var secondScheduleResponse = await client.PostAsJsonAsync(
            $"/api/policies/{secondPolicy!.PolicyId}/schedule", new ScheduleRequest(0m, 1));
        secondScheduleResponse.EnsureSuccessStatusCode();

        AgencyContext.Current = fixture.AgencyAId;
        var firstInstallmentId = await seedContext.Installments
            .Where(i => i.PolicyId == firstPolicy.PolicyId).OrderBy(i => i.SeqNo).Select(i => i.Id).FirstAsync();

        var paymentResponse = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            firstInstallmentId, 4_000_000m, today, "نقدی", "REF-1"));
        paymentResponse.EnsureSuccessStatusCode();

        var policyFile = await client.GetFromJsonAsync<PolicyFileDto>($"/api/policies/{firstPolicy.PolicyId}/file");
        Assert.NotNull(policyFile);
        Assert.Equal(2, policyFile!.Installments.Count);
        // History tab is a single indexed query on AuditEntry.PolicyId — every row here (issuance,
        // scheduling, the payment) belongs to POLICY 1 only, never policy 2's.
        Assert.Contains(policyFile.Timeline, t => t.Description.Contains("ثبت پرداخت قسط"));
        Assert.All(policyFile.Timeline, t => Assert.NotEmpty(t.ActorDisplayName));

        var customerFile = await client.GetFromJsonAsync<CustomerFileDto>($"/api/customers/{policyFile.CustomerId}/file");
        Assert.NotNull(customerFile);
        Assert.Equal(2, customerFile!.Policies.Count);
        // Policy 1 balance: 8,000,000 receivable - 4,000,000 paid = 4,000,000. Policy 2: 6,000,000 untouched.
        Assert.Equal(10_000_000m, customerFile.AggregateBalance);
        // Two payments now: the 2,000,000 down-payment receipt from receive-down-payment above,
        // plus the explicit 4,000,000 installment payment recorded above.
        Assert.Equal(2, customerFile.Payments.Count);
        var installmentPayment = Assert.Single(customerFile.Payments, p => p.ReferenceNo == "REF-1");
        Assert.NotEmpty(installmentPayment.AllocatedTo);
        var downPaymentReceipt = Assert.Single(customerFile.Payments, p => p.Amount == 2_000_000m);
        Assert.Empty(downPaymentReceipt.AllocatedTo);
        // Unified timeline merges both policies — it must have strictly more rows than policy 1's
        // own file, since policy 2's issuance/scheduling entries are included too.
        Assert.True(customerFile.Timeline.Count > policyFile.Timeline.Count);
        Assert.Contains(customerFile.Timeline, t => t.Description.Contains("ثبت پرداخت قسط"));
    }
}
