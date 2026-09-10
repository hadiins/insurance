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

namespace Aqsat.UnitTests.Reports;

/// <summary>«پرداخت به بیمه‌گر» — a settled installment shows up as pending remittance, gets removed
/// from the pending list once remitted, and a second attempt to remit the same installment is
/// rejected (the settlement-deadline discipline this whole feature exists for).</summary>
[Collection("WebApplicationFactory")]
public class InsurerRemittanceEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public InsurerRemittanceEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Settled_installment_appears_pending_then_disappears_once_remitted_and_cannot_be_remitted_twice()
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

        AgencyContext.Current = fixture.AgencyAId;
        var bankAccount = new BankAccount { AgencyId = fixture.AgencyAId, BankName = "بانک ملی", AccountNumber = "1234567890", IsActive = true };
        seedContext.BankAccounts.Add(bankAccount);
        await seedContext.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-REM-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری واریز بیمه‌گر", null, null,
            Vehicle: new VehicleInput("۵۵ه۵۵۵", null, null, null, null, null), Property: null,
            today, today, today.AddYears(1), 2_000_000m, 0m, null, null, false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var scheduleResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(0m, 1));
        scheduleResponse.EnsureSuccessStatusCode();

        var installmentId = await seedContext.Installments
            .Where(i => i.PolicyId == policy.PolicyId).Select(i => i.Id).FirstAsync();
        var paymentResponse = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installmentId, 2_000_000m, today, "نقدی", null));
        paymentResponse.EnsureSuccessStatusCode();

        var pendingBefore = await client.GetFromJsonAsync<List<PendingRemittanceRow>>("/api/insurer-remittances/pending");
        Assert.Contains(pendingBefore!, r => r.InstallmentId == installmentId);

        var createResponse = await client.PostAsJsonAsync("/api/insurer-remittances", new CreateInsurerRemittanceRequest(
            today, PaymentMethod.BankTransfer, null, bankAccount.Id, "REF-REMIT-1",
            [new RemittanceLineRequest(policy.PolicyId, installmentId, 2_000_000m)]));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<InsurerRemittanceDto>();
        Assert.Equal(2_000_000m, created!.Amount);
        Assert.Single(created.Lines);

        var pendingAfter = await client.GetFromJsonAsync<List<PendingRemittanceRow>>("/api/insurer-remittances/pending");
        Assert.DoesNotContain(pendingAfter!, r => r.InstallmentId == installmentId);

        var duplicateResponse = await client.PostAsJsonAsync("/api/insurer-remittances", new CreateInsurerRemittanceRequest(
            today, PaymentMethod.BankTransfer, null, bankAccount.Id, "REF-REMIT-2",
            [new RemittanceLineRequest(policy.PolicyId, installmentId, 2_000_000m)]));
        Assert.False(duplicateResponse.IsSuccessStatusCode);

        var list = await client.GetFromJsonAsync<List<InsurerRemittanceDto>>("/api/insurer-remittances");
        Assert.Contains(list!, r => r.Id == created.Id);
    }
}
