using System.Net;
using Aqsat.Application.Common;
using Aqsat.Application.Schedule;


using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Policies;

/// <summary>
/// Task 6's own check (docs/TASKS.md): create a fire (آتش‌سوزی) policy with no vehicle — accepted.
/// Create a ثالث with no vehicle — rejected with a clear Persian message. RequiresVehicle/
/// RequiresProperty is enforced at the service layer, not just the frontend form.
/// </summary>
[Collection("WebApplicationFactory")]
public class PolicyIssuanceEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PolicyIssuanceEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Third_party_policy_without_a_vehicle_is_rejected_with_a_clear_persian_message()
    {
        var (client, salisLineId, _) = await SeedAsync();

        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری آزمایشی", null, null,
            Vehicle: null, Property: null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            9_000_000m, 500_000m, null, null, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("خودرو", problem!.Title);
    }

    [Fact]
    public async Task Fire_policy_without_a_vehicle_is_accepted_when_property_details_are_given()
    {
        var (client, _, atashLineId) = await SeedAsync();

        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{Guid.NewGuid():N}"[..16], atashLineId, null, "مشتری آتش‌سوزی", "09120000001", null,
            Vehicle: null,
            Property: new PropertySubjectInput("تهران، خیابان آزادی", "1234567890", "مسکونی", 2_000_000_000m),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            50_000_000m, 1_000_000m, null, null, false));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreatePolicyResultDto>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result!.PolicyId);
    }

    [Fact]
    public async Task Third_party_policy_with_a_vehicle_is_accepted_and_totals_are_computed_correctly()
    {
        var (client, salisLineId, _) = await SeedAsync();

        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری ثالث", null, null,
            Vehicle: new VehicleInput("۱۱الف۱۱۱", null, null, null, null, null),
            Property: null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            9_000_000m, 500_000m, null, null, false));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = (await client.GetFromJsonAsync<MeResponse>("/api/auth/me"))!.ActiveOrganizationId;
        var policy = await verify.Policies.AsNoTracking().SingleAsync(p => p.Id == result!.PolicyId);
        Assert.Equal(9_500_000m, policy.TotalReceivable);
        Assert.NotNull(policy.VehicleId);
    }

    /// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §5.3 — the structured plate component's four
    /// parts, composed server-side into PlateNormalized, digits normalized to Latin.</summary>
    [Fact]
    public async Task Structured_plate_parts_are_composed_into_plate_normalized()
    {
        var (client, salisLineId, _) = await SeedAsync();

        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری پلاک", null, null,
            Vehicle: new VehicleInput(
                null, null, null, null, null, null,
                PlateType: 1, PlateTwoDigit: "۵۵", PlateLetter: "الف", PlateThreeDigit: "۵۵۵", PlateIranCode: "۶۳"),
            Property: null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            9_000_000m, 500_000m, null, null, false));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = (await client.GetFromJsonAsync<MeResponse>("/api/auth/me"))!.ActiveOrganizationId;
        var policy = await verify.Policies.AsNoTracking().Include(p => p.Vehicle).SingleAsync(p => p.Id == result!.PolicyId);
        Assert.Equal("55الف555-63", policy.Vehicle!.PlateNormalized);
        Assert.Equal("55", policy.Vehicle.PlateTwoDigit);
        Assert.Equal("555", policy.Vehicle.PlateThreeDigit);
        Assert.Equal("63", policy.Vehicle.PlateIranCode);
    }

    /// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §3 — a customer entered fully from the issuance
    /// form (all of FirstName/LastName/NationalId/Mobile/Address/PostalCode) must actually compute
    /// as IsProfileComplete, not silently land in the completion queue for fields this form never
    /// asked for.</summary>
    [Fact]
    public async Task Fully_filled_customer_fields_from_issuance_compute_as_profile_complete()
    {
        var (client, salisLineId, _) = await SeedAsync();

        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{Guid.NewGuid():N}"[..16], salisLineId, null, "مجید حیدری", "09123456789", "0072345454",
            Vehicle: new VehicleInput("۱۱ی۲۲۲", null, null, null, null, null), Property: null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            9_000_000m, 500_000m, null, null, false,
            CustomerFirstName: "مجید", CustomerLastName: "حیدری",
            CustomerAddress: "تهران، خیابان آزادی، پلاک ۱", CustomerPostalCode: "1234567890"));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = (await client.GetFromJsonAsync<MeResponse>("/api/auth/me"))!.ActiveOrganizationId;
        var customer = await verify.Customers.AsNoTracking().SingleAsync(c => c.Id == result!.CustomerId);
        Assert.Equal("مجید", customer.FirstName);
        Assert.Equal("حیدری", customer.LastName);
        Assert.True(customer.IsProfileComplete);
    }

    [Fact]
    public async Task Structured_plate_parts_with_non_digit_content_are_rejected()
    {
        // B15 — the plate parts used to be digit-NORMALIZED but never digit-VALIDATED: "ab" would
        // land in PlateTwoDigit and poison every plate search keyed on the parts.
        var (client, salisLineId, _) = await SeedAsync();

        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری پلاک", null, null,
            Vehicle: new VehicleInput(
                null, null, null, null, null, null,
                PlateType: 1, PlateTwoDigit: "ab", PlateLetter: "ب", PlateThreeDigit: "345", PlateIranCode: "11"),
            Property: null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            9_000_000m, 500_000m, null, null, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("عدد", problem!.Title);
    }

    [Fact]
    public async Task A_partial_structured_plate_is_rejected()
    {
        // Without all four parts the composed plate is silently dropped altogether — Vehicle.Plate
        // stays null while the agent believes they entered a plate.
        var (client, salisLineId, _) = await SeedAsync();

        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری پلاک ناقص", null, null,
            Vehicle: new VehicleInput(
                null, null, null, null, null, null,
                PlateType: 1, PlateTwoDigit: "12", PlateLetter: "ب", PlateThreeDigit: "345", PlateIranCode: null),
            Property: null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            9_000_000m, 500_000m, null, null, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("ناقص", problem!.Title);
    }

    [Fact]
    public async Task An_oversized_policy_number_is_rejected_up_front_not_as_a_sql_truncation_500()
    {
        // B14 — PolicyNumber's column is nvarchar(40); without the contract cap a longer value
        // reached SQL Server and blew up as a raw 500.
        var (client, salisLineId, _) = await SeedAsync();

        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            new string('9', 100), salisLineId, null, "مشتری شمارهٔ بلند", null, null,
            Vehicle: null, Property: null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            9_000_000m, 500_000m, null, null, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Equal("درخواست ارسالی نامعتبر است.", problem!.Title);
    }

    [Fact]
    public async Task Unknown_payment_type_is_rejected_and_omitted_installment_mode_always_arms_verification()
    {
        var (client, lineId, _) = await SeedAsync();
        var today = IranClock.Today();

        var invalid = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-BAD-{Guid.NewGuid():N}"[..16], lineId, null, "مشتری تست", null, null,
            Vehicle: new VehicleInput("11الف111", null, null, null, null, null), Property: null,
            today, today, today.AddYears(1), 1_000_000m, 0m, null, null, false,
            PaymentType: "installment-but-skip-verification"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("نوع پرداخت", await invalid.Content.ReadAsStringAsync());

        var implicitInstallment = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-IMP-{Guid.NewGuid():N}"[..16], lineId, null, "مشتری تست", null, null,
            Vehicle: new VehicleInput("22ب222", null, null, null, null, null), Property: null,
            today, today, today.AddYears(1), 1_000_000m, 0m, null, null, false));
        implicitInstallment.EnsureSuccessStatusCode();
        var result = await implicitInstallment.Content.ReadFromJsonAsync<CreatePolicyResultDto>();
        AgencyContext.Current = (await client.GetFromJsonAsync<MeResponse>("/api/auth/me"))!.ActiveOrganizationId;
        await using var verify = TestDbContextFactory.Create();
        var row = await verify.Policies.AsNoTracking().SingleAsync(p => p.Id == result!.PolicyId);
        Assert.True(row.IsInstallment);
        Assert.True(row.RequiresVerification);
    }

    [Fact]
    public async Task Cash_full_payment_must_equal_total_and_cannot_be_scheduled_or_paid_after_cancel()
    {
        var (client, lineId, _) = await SeedAsync();
        var today = IranClock.Today();
        var created = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-CASH-{Guid.NewGuid():N}"[..16], lineId, null, "مشتری نقدی", null, null,
            Vehicle: new VehicleInput("33ج333", null, null, null, null, null), Property: null,
            today, today, today.AddYears(1), 1_000_000m, 100_000m, null, null, false,
            PaymentType: "cash"));
        created.EnsureSuccessStatusCode();
        var result = await created.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var schedule = await client.PostAsJsonAsync($"/api/policies/{result!.PolicyId}/schedule",
            new ScheduleRequest(0m, 1));
        Assert.Equal(HttpStatusCode.BadRequest, schedule.StatusCode);

        var boxes = await client.GetFromJsonAsync<List<CashBoxDto>>("/api/settings/cash-and-bank/cash-boxes");
        var cashBoxId = Assert.Single(boxes!.Where(c => c.IsActive)).Id;
        var shortPay = await client.PostAsJsonAsync($"/api/policies/{result.PolicyId}/record-full-payment",
            new RecordFullPaymentRequest(1, today, "نقدی", null,
                MethodType: Aqsat.Domain.Enums.PaymentMethod.Cash, CashBoxId: cashBoxId));
        Assert.Equal(HttpStatusCode.BadRequest, shortPay.StatusCode);

        var fullPay = await client.PostAsJsonAsync($"/api/policies/{result.PolicyId}/record-full-payment",
            new RecordFullPaymentRequest(1_100_000m, today, "نقدی", null,
                MethodType: Aqsat.Domain.Enums.PaymentMethod.Cash, CashBoxId: cashBoxId));
        fullPay.EnsureSuccessStatusCode();
        var finalize = await client.PostAsync($"/api/policies/{result.PolicyId}/finalize", null);
        finalize.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Vehicle_manufacture_year_accepts_jalali_and_gregorian_and_stores_gregorian()
    {
        var (client, lineId, _) = await SeedAsync();
        var today = IranClock.Today();

        async Task<int?> CreateAsync(int inputYear, string plate)
        {
            var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
                $"POL-YR-{Guid.NewGuid():N}"[..16], lineId, null, "مشتری سال خودرو", null, null,
                Vehicle: new VehicleInput(plate, null, null, null, null, null,
                    ManufactureYear: inputYear),
                Property: null, today, today, today.AddYears(1),
                1_000_000m, 0m, null, null, false, PaymentType: "cash"));
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<CreatePolicyResultDto>();
            AgencyContext.Current = (await client.GetFromJsonAsync<MeResponse>("/api/auth/me"))!.ActiveOrganizationId;
            await using var verify = TestDbContextFactory.Create();
            return await verify.Vehicles.AsNoTracking()
                .Where(v => v.Id == verify.Policies.Where(p => p.Id == result!.PolicyId).Select(p => p.VehicleId).Single())
                .Select(v => v.ManufactureYear).SingleAsync();
        }

        Assert.Equal(2025, await CreateAsync(1404, "11الف111"));
        Assert.Equal(2025, await CreateAsync(2025, "22ب222"));

        var invalid = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-YR-{Guid.NewGuid():N}"[..16], lineId, null, "مشتری سال نامعتبر", null, null,
            Vehicle: new VehicleInput("33ج333", null, null, null, null, null, ManufactureYear: 1200),
            Property: null, today, today, today.AddYears(1),
            1_000_000m, 0m, null, null, false, PaymentType: "cash"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("شمسی یا میلادی", await invalid.Content.ReadAsStringAsync());
    }

    private async Task<(HttpClient Client, Guid SalisLineId, Guid AtashLineId)> SeedAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);

        var salisLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();
        var atashLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == "ATASH").Select(l => l.Id).FirstAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        return (client, salisLineId, atashLineId);
    }

    [Fact]
    public void Policy_end_date_is_one_jalali_year_from_start_not_from_issue_date()
    {
        // 2025-03-20 is 1403/12/30; +12 Jalali months is 1404/12/30, not a Gregorian year.
        Assert.Equal(new DateOnly(2026, 3, 20),
            DueDateCalculator.CalculatePolicyEndDate(new DateOnly(2025, 3, 20)));
        Assert.Equal(new DateOnly(2029, 3, 20),
            DueDateCalculator.CalculatePolicyEndDate(new DateOnly(2028, 3, 20)));
    }

    private sealed record CashBoxDto(Guid Id, string Name, bool IsActive);

    private sealed record ProblemDetailsDto(string Title);
}
