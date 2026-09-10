using System.Net;
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
/// The cheque-module deep review's fixes: entry-mistake guards on cheque details at every receipt
/// path (a forgotten cheque number or cash box used to surface as a raw 500 — NullReferenceException
/// on Trim, FK violation on Guid.Empty), and Bounced as a terminal cheque status — once the payment
/// is reversed, no status edit can show a healthy cheque on top of a reversed ledger.
/// </summary>
[Collection("WebApplicationFactory")]
public class ChequeGuardEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ChequeGuardEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task An_empty_cheque_number_is_rejected_with_a_persian_reason_not_saved_as_is()
    {
        // An empty string is what a forgotten input actually sends — unlike JSON null, it passes
        // model binding and used to reach the database as ChequeNumber = "".
        var (client, installmentId, cashBoxId, _, _) = await SeedChequeTargetAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var response = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installmentId, 4_000_000m, today, "چک", null,
            MethodType: PaymentMethod.Cheque,
            Cheque: new ChequeDetailsRequest("", "بانک ملت", today.AddMonths(1), "علی رضایی", cashBoxId)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("شمارهٔ چک", problem!.Title);
    }

    [Fact]
    public async Task An_unselected_cash_box_is_rejected_before_reaching_the_database()
    {
        var (client, installmentId, _, _, _) = await SeedChequeTargetAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var response = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installmentId, 4_000_000m, today, "چک", null,
            MethodType: PaymentMethod.Cheque,
            Cheque: new ChequeDetailsRequest("CHQ-1", "بانک ملت", today.AddMonths(1), "علی رضایی", Guid.Empty)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("صندوق", problem!.Title);
    }

    [Fact]
    public async Task A_cash_box_outside_the_agency_is_rejected_as_not_found_not_an_fk_500()
    {
        var (client, installmentId, _, _, _) = await SeedChequeTargetAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var response = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installmentId, 4_000_000m, today, "چک", null,
            MethodType: PaymentMethod.Cheque,
            Cheque: new ChequeDetailsRequest("CHQ-1", "بانک ملت", today.AddMonths(1), "علی رضایی", Guid.NewGuid())));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("صندوق انتخاب‌شده یافت نشد", problem!.Title);
    }

    [Fact]
    public async Task A_cheque_due_date_years_ahead_is_rejected()
    {
        var (client, installmentId, cashBoxId, _, _) = await SeedChequeTargetAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var response = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installmentId, 4_000_000m, today, "چک", null,
            MethodType: PaymentMethod.Cheque,
            Cheque: new ChequeDetailsRequest("CHQ-1", "بانک ملت", today.AddYears(5), "علی رضایی", cashBoxId)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("سررسید", problem!.Title);
    }

    [Fact]
    public async Task A_bounced_cheque_is_terminal_no_status_edit_can_revive_it()
    {
        var (client, installmentId, cashBoxId, seedContext, fixture) = await SeedChequeTargetAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var cheque = new ChequeDetailsRequest("CHQ-002", "بانک ملت", today.AddMonths(1), "علی رضایی", cashBoxId);
        var paymentResponse = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installmentId, 4_000_000m, today, "چک", null, MethodType: PaymentMethod.Cheque, Cheque: cheque));
        paymentResponse.EnsureSuccessStatusCode();
        var paymentResult = await paymentResponse.Content.ReadFromJsonAsync<PaymentResultDto>();

        AgencyContext.Current = fixture.AgencyAId;
        var paymentCheque = await seedContext.PaymentCheques.AsNoTracking()
            .SingleAsync(c => c.PaymentId == paymentResult!.PaymentId);

        var bounce = await client.PutAsJsonAsync(
            $"/api/payment-cheques/{paymentCheque.Id}/status", new UpdatePaymentChequeStatusRequest("Bounced"));
        bounce.EnsureSuccessStatusCode();

        // The trap the fix closes: "it actually cleared at the bank, let me set it back" would
        // leave a healthy-looking cheque on top of a reversed payment with no un-reverse path.
        var revive = await client.PutAsJsonAsync(
            $"/api/payment-cheques/{paymentCheque.Id}/status", new UpdatePaymentChequeStatusRequest("Held"));
        Assert.Equal(HttpStatusCode.BadRequest, revive.StatusCode);
        var problem = await revive.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("چک برگشتی پایانی است", problem!.Title);

        AgencyContext.Current = fixture.AgencyAId;
        var stillBounced = await seedContext.PaymentCheques.AsNoTracking().SingleAsync(c => c.Id == paymentCheque.Id);
        Assert.Equal(CollateralStatus.Bounced, stillBounced.Status);
    }

    [Fact]
    public async Task A_free_text_payment_method_is_rejected()
    {
        // B16 — Payment.Method is a report-grouping column; only the four labels the receipt form
        // pairs with a PaymentMethod choice are acceptable from a client.
        var (client, installmentId, _, _, _) = await SeedChequeTargetAsync();

        var response = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installmentId, 500_000m, DateOnly.FromDateTime(DateTime.UtcNow), "هر متنی که کلاینت فرستاد", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("روش پرداخت", problem!.Title);
    }

    [Fact]
    public async Task A_batch_payment_request_over_the_cap_is_rejected_whole()
    {
        // B13 — one SaveChanges per item; an unbounded array would hold the request thread and its
        // DB connection for as long as thousands of payments take.
        var (client, installmentId, _, _, _) = await SeedChequeTargetAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var items = Enumerable.Range(0, 201)
            .Select(_ => new RecordPaymentRequest(installmentId, 1m, today, "نقدی", null))
            .ToList();

        var response = await client.PostAsJsonAsync("/api/payments/batch", items);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("۲۰۰", problem!.Title);
    }

    /// <summary>Customer + vehicle + cash box + single-installment policy, the exact shape a
    /// cheque receipt needs; the live seed context comes back for post-call assertions.</summary>
    private async Task<(HttpClient Client, Guid InstallmentId, Guid CashBoxId, AppDbContext SeedContext, DevSeeder.SeededAuthFixture Fixture)> SeedChequeTargetAsync()
    {
        var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);

        AgencyContext.Current = fixture.AgencyAId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var customer = new Customer { AgencyId = fixture.AgencyAId, ExternalCode = $"EXT-{Guid.NewGuid():N}"[..12], FullName = "مشتری چک" };
        var vehicle = new Vehicle { AgencyId = fixture.AgencyAId, Plate = "77ج777" };
        var cashBox = new CashBox { AgencyId = fixture.AgencyAId, Name = "صندوق مرکزی", IsActive = true };
        seedContext.Customers.Add(customer);
        seedContext.Vehicles.Add(vehicle);
        seedContext.CashBoxes.Add(cashBox);
        await seedContext.SaveChangesAsync();

        var lineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var policy = new Policy
        {
            AgencyId = fixture.AgencyAId,
            PolicyNumber = $"POL-CHQG-{Guid.NewGuid():N}"[..17],
            InsuranceLineId = lineId,
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

        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        return (client, installment.Id, cashBox.Id, seedContext, fixture);
    }

    private sealed record ProblemDetailsDto(string Title);
}
