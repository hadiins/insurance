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
/// A customer paying in person at the agency's own card terminal, wired directly to the insurer's
/// account — the money never touches the agency's CashBox/BankAccount, so PaymentMethod.PosDirect
/// must be recordable with neither, while still settling the installment.
/// </summary>
[Collection("WebApplicationFactory")]
public class PosDirectPaymentEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PosDirectPaymentEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Recording_a_pos_direct_payment_needs_no_cashbox_or_bank_account_and_settles_the_installment()
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var customer = new Customer { AgencyId = fixture.AgencyAId, ExternalCode = $"EXT-{Guid.NewGuid():N}"[..12], FullName = "مشتری پوز" };
        var vehicle = new Vehicle { AgencyId = fixture.AgencyAId, Plate = "99و999" };
        seedContext.Customers.Add(customer);
        seedContext.Vehicles.Add(vehicle);
        await seedContext.SaveChangesAsync();

        var policy = new Policy
        {
            AgencyId = fixture.AgencyAId,
            PolicyNumber = $"POL-POS-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = salisLineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = today,
            StartDate = today,
            EndDate = today.AddYears(1),
            NetPremium = 3_000_000m,
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
            Amount = 3_000_000m,
            Status = InstallmentStatus.Unpaid,
        };
        seedContext.Installments.Add(installment);
        await seedContext.SaveChangesAsync();

        var response = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installment.Id, 3_000_000m, today, "پوز مستقیم بیمه‌گر", "TERM-42",
            MethodType: PaymentMethod.PosDirect));
        response.EnsureSuccessStatusCode();

        AgencyContext.Current = fixture.AgencyAId;
        var settled = await seedContext.Installments.AsNoTracking().SingleAsync(i => i.Id == installment.Id);
        Assert.Equal(InstallmentStatus.Settled, settled.Status);

        var payment = await seedContext.Payments.AsNoTracking().SingleAsync(p => p.InstallmentIdHint == installment.Id);
        Assert.Equal(PaymentMethod.PosDirect, payment.MethodType);
        Assert.Null(payment.CashBoxId);
        Assert.Null(payment.BankAccountId);

        var report = await client.GetFromJsonAsync<ReceiptsReportDto>(
            $"/api/reports/receipts?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.NotNull(report);
        var row = Assert.Single(report!.Rows, r => r.PaymentId == payment.Id);
        Assert.Equal("PosDirect", row.MethodType);
        Assert.Null(row.CashBoxName);
        Assert.Null(row.BankAccountLabel);
    }
}
