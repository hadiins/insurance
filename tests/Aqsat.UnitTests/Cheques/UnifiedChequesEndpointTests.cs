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

namespace Aqsat.UnitTests.Cheques;

/// <summary>GET /api/cheques — the unified view behind «همهٔ چک‌ها». Guarantee cheques (Collateral)
/// and cheques received toward a payment (PaymentCheque) live in two tables; this view must merge
/// both so nothing hides from «چک‌های پیشِ رو» / «چک‌های برگشتی». Status updates stay on the
/// per-source endpoints — PaymentCheque's bounce-driven reversal is the single unwind path.</summary>
[Collection("WebApplicationFactory")]
public class UnifiedChequesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UnifiedChequesEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Both_sources_appear_filtered_by_status_and_window_and_are_hidden_from_other_agencies()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var lineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        // RLS has a block predicate on CashBoxes — the seed context must act as agency A to insert.
        AgencyContext.Current = fixture.AgencyAId;
        var cashBox = new CashBox { AgencyId = fixture.AgencyAId, Name = "صندوق چک تست" };
        seedContext.CashBoxes.Add(cashBox);
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        // The "upcoming" filter compares against the real wall clock — anchor due dates to actual "now".
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-UQ-{Guid.NewGuid():N}"[..16], lineId, null, "بیمه‌گذار چک واحد", null, null,
            Vehicle: new VehicleInput("۸۸ط۴۴۴", null, null, null, null, null), Property: null,
            today, today, today.AddYears(1), 9_000_000m, 0m, null, null, false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        await client.PostAsJsonAsync($"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(0m, 3));

        // Source 1: a guarantee cheque registered on the collateral page.
        var collateralResponse = await client.PostAsJsonAsync("/api/collateral", new CreateCollateralRequest(
            policy.PolicyId, "ChequeSayadi", "IR010203040506070809101112", "بانک ملی", 3_000_000m, today.AddDays(20)));
        collateralResponse.EnsureSuccessStatusCode();
        var collateral = await collateralResponse.Content.ReadFromJsonAsync<CollateralDto>();

        // Source 2: a cheque received while recording an installment payment.
        var installments = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>(
            $"/api/installments?search={policy.PolicyNumber}");
        var installment = installments!.First(i => i.PolicyId == policy.PolicyId);
        var paymentResponse = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installment.InstallmentId, 1_000_000m, today, "چک", null,
            MethodType: PaymentMethod.Cheque,
            Cheque: new ChequeDetailsRequest("654321", "بانک سپه", today.AddDays(25), "معرف تستی", cashBox.Id)));
        paymentResponse.EnsureSuccessStatusCode();

        var all = await client.GetFromJsonAsync<List<UnifiedChequeRowDto>>("/api/cheques");
        var collateralRow = all!.Single(r => r.Source == "Collateral" && r.Id == collateral!.Id);
        var paymentRow = all!.Single(r => r.Source == "Payment" && r.ChequeNumber == "654321");
        Assert.Equal(policy.PolicyNumber, collateralRow.PolicyNumber);
        Assert.Equal("IR010203040506070809101112", collateralRow.SayadId);
        Assert.Equal("معرف تستی", paymentRow.PresenterName);
        Assert.Equal(1_000_000m, paymentRow.Amount);
        Assert.Equal("Held", paymentRow.Status);

        // The upcoming window admits the collateral cheque (due in 20 days) but not the payment
        // cheque (due in 25) — a 22-day horizon must keep exactly the first.
        var narrowWindow = await client.GetFromJsonAsync<List<UnifiedChequeRowDto>>("/api/cheques?upcomingDays=22");
        Assert.Contains(narrowWindow!, r => r.Id == collateral!.Id && r.Source == "Collateral");
        Assert.DoesNotContain(narrowWindow!, r => r.Source == "Payment" && r.ChequeNumber == "654321");

        // Status filtering sees a bounced payment cheque — via the per-source endpoint, which is
        // also what drives the reversal.
        var bounce = await client.PutAsJsonAsync(
            $"/api/payment-cheques/{paymentRow.Id}/status", new UpdatePaymentChequeStatusRequest("Bounced"));
        Assert.Equal(HttpStatusCode.OK, bounce.StatusCode);

        var bounced = await client.GetFromJsonAsync<List<UnifiedChequeRowDto>>("/api/cheques?status=Bounced");
        Assert.Contains(bounced!, r => r.Id == paymentRow.Id && r.Source == "Payment");

        var held = await client.GetFromJsonAsync<List<UnifiedChequeRowDto>>("/api/cheques?status=Held");
        Assert.Contains(held!, r => r.Id == collateral!.Id);
        Assert.DoesNotContain(held!, r => r.Id == paymentRow.Id);

        // RLS: the same dual-agency user acting in agency B sees none of agency A's cheques.
        var clientB = _factory.CreateClient();
        var loginB = await clientB.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginB.EnsureSuccessStatusCode();
        var tokenB = (await loginB.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        clientB.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyBId.ToString());

        var agencyBRows = await clientB.GetFromJsonAsync<List<UnifiedChequeRowDto>>("/api/cheques");
        Assert.DoesNotContain(agencyBRows!, r => r.Id == collateral!.Id || r.Id == paymentRow.Id);
    }
}
