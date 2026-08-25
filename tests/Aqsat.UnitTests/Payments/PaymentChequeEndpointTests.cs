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

namespace Aqsat.UnitTests.Payments;

/// <summary>
/// Stage 5/7 — چک بابت قسط: cheque details are required when MethodType == Cheque, the cheque is
/// held in the chosen CashBox, and bouncing it (PUT /payment-cheques/{id}/status) drives exactly
/// the same unwind a manual reversal does — installment reopens, commission slice goes back Pending.
/// </summary>
[Collection("WebApplicationFactory")]
public class PaymentChequeEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PaymentChequeEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Cheque_payment_without_details_is_rejected_and_with_details_settles_and_bounces_correctly()
    {
        var (fixture, seedContext, client) = await SeedAsync();

        AgencyContext.Current = fixture.AgencyAId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var customer = new Customer { AgencyId = fixture.AgencyAId, ExternalCode = $"EXT-{Guid.NewGuid():N}"[..12], FullName = "مشتری چک" };
        var vehicle = new Vehicle { AgencyId = fixture.AgencyAId, Plate = "77ج777" };
        var cashBox = new CashBox { AgencyId = fixture.AgencyAId, Name = "صندوق مرکزی", IsActive = true };
        seedContext.Customers.Add(customer);
        seedContext.Vehicles.Add(vehicle);
        seedContext.CashBoxes.Add(cashBox);
        await seedContext.SaveChangesAsync();

        var salisLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var policy = new Policy
        {
            AgencyId = fixture.AgencyAId,
            PolicyNumber = $"POL-CHQ-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = salisLineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = today,
            StartDate = today,
            EndDate = today.AddYears(1),
            NetPremium = 4_000_000m,
            DownPayment = 0,
            InstallmentCount = 1,
        };
        seedContext.Policies.Add(policy);
        await seedContext.SaveChangesAsync();

        var installment = new Installment
        {
            AgencyId = fixture.AgencyAId,
            PolicyId = policy.Id,
            SeqNo = 1,
            DueDate = today,
            SettlementDeadline = today.AddDays(3),
            Amount = 4_000_000m,
            Status = InstallmentStatus.Unpaid,
        };
        seedContext.Installments.Add(installment);
        await seedContext.SaveChangesAsync();

        // Missing cheque details must be rejected, never silently defaulted.
        var rejectedResponse = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installment.Id, 4_000_000m, today, "چک", null, MethodType: PaymentMethod.Cheque));
        Assert.False(rejectedResponse.IsSuccessStatusCode);

        var cheque = new ChequeDetailsRequest("CHQ-001", "بانک ملت", today.AddMonths(1), "علی رضایی", cashBox.Id);
        var paymentResponse = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installment.Id, 4_000_000m, today, "چک", null, MethodType: PaymentMethod.Cheque, Cheque: cheque));
        paymentResponse.EnsureSuccessStatusCode();
        var paymentResult = await paymentResponse.Content.ReadFromJsonAsync<PaymentResultDto>();

        AgencyContext.Current = fixture.AgencyAId;
        var settledInstallment = await seedContext.Installments.AsNoTracking().SingleAsync(i => i.Id == installment.Id);
        Assert.Equal(InstallmentStatus.Settled, settledInstallment.Status);

        var paymentCheque = await seedContext.PaymentCheques.AsNoTracking().SingleAsync(c => c.PaymentId == paymentResult!.PaymentId);
        Assert.Equal("CHQ-001", paymentCheque.ChequeNumber);
        Assert.Equal(CollateralStatus.Held, paymentCheque.Status);
        Assert.Equal(policy.Id, paymentCheque.PolicyId);
        Assert.Equal(cashBox.Id, paymentCheque.CashBoxId);

        // Bouncing the cheque must unwind the settlement — same effect a manual reversal has.
        var bounceResponse = await client.PutAsJsonAsync(
            $"/api/payment-cheques/{paymentCheque.Id}/status", new UpdatePaymentChequeStatusRequest("Bounced"));
        bounceResponse.EnsureSuccessStatusCode();

        var reopenedInstallment = await seedContext.Installments.AsNoTracking().SingleAsync(i => i.Id == installment.Id);
        Assert.Equal(InstallmentStatus.Unpaid, reopenedInstallment.Status);
        Assert.Equal(0m, reopenedInstallment.PaidAmount);

        var bouncedCheque = await seedContext.PaymentCheques.AsNoTracking().SingleAsync(c => c.Id == paymentCheque.Id);
        Assert.Equal(CollateralStatus.Bounced, bouncedCheque.Status);

        var reversedPayment = await seedContext.Payments.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(p => p.Id == paymentResult!.PaymentId);
        Assert.True(reversedPayment.IsDeleted);
    }

    private async Task<(DevSeeder.SeededAuthFixture Fixture, AppDbContext SeedContext, HttpClient Client)> SeedAsync()
    {
        var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        return (fixture, seedContext, client);
    }
}
