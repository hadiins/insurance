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

namespace Aqsat.UnitTests.Installments;

/// <summary>
/// Task 9's own check (docs/TASKS.md, niaz #3): editing one installment's amount recomputes only
/// that installment; editing its due date reapplies the holiday-shift rule to its deadline.
/// </summary>
[Collection("WebApplicationFactory")]
public class InstallmentEditEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public InstallmentEditEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Editing_the_amount_flags_manual_edit_and_leaves_other_installments_untouched()
    {
        var (client, installments) = await SeedPolicyWithInstallmentsAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/installments/{installments[0].Id}", new UpdateInstallmentRequest(1_200_000m, null));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<InstallmentDetailDto>();

        Assert.Equal(1_200_000m, result!.Amount);
        Assert.True(result.IsManuallyEdited);
        Assert.Equal(installments[0].DueDate, result.DueDate);

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = installments[0].AgencyId;
        var untouched = await verify.Installments.AsNoTracking().SingleAsync(i => i.Id == installments[1].Id);
        Assert.Equal(1_000_000m, untouched.Amount);
        Assert.False(untouched.IsManuallyEdited);
    }

    [Fact]
    public async Task Editing_the_due_date_recomputes_the_settlement_deadline_with_the_holiday_rule()
    {
        var (client, installments) = await SeedPolicyWithInstallmentsAsync();

        // 2026-08-18 + the default 3-day deadline lands on Friday 2026-08-21 -> must shift to Saturday.
        var newDueDate = new DateOnly(2026, 8, 18);
        var response = await client.PutAsJsonAsync(
            $"/api/installments/{installments[0].Id}", new UpdateInstallmentRequest(null, newDueDate));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<InstallmentDetailDto>();

        Assert.Equal(newDueDate, result!.DueDate);
        Assert.Equal(new DateOnly(2026, 8, 22), result.SettlementDeadline);
    }

    [Fact]
    public async Task A_settled_installment_cannot_be_edited()
    {
        var (client, installments) = await SeedPolicyWithInstallmentsAsync();

        await using var seedContext = TestDbContextFactory.Create();
        AgencyContext.Current = installments[0].AgencyId;
        var settled = await seedContext.Installments.SingleAsync(i => i.Id == installments[0].Id);
        settled.PaidAmount = settled.Amount;
        settled.Status = InstallmentStatus.Settled;
        await seedContext.SaveChangesAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/installments/{installments[0].Id}", new UpdateInstallmentRequest(2_000_000m, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Amount_cannot_be_reduced_below_the_already_paid_amount()
    {
        var (client, installments) = await SeedPolicyWithInstallmentsAsync();

        await using var seedContext = TestDbContextFactory.Create();
        AgencyContext.Current = installments[0].AgencyId;
        var partial = await seedContext.Installments.SingleAsync(i => i.Id == installments[0].Id);
        partial.PaidAmount = 600_000m;
        partial.Status = InstallmentStatus.Partial;
        await seedContext.SaveChangesAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/installments/{installments[0].Id}", new UpdateInstallmentRequest(500_000m, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<(HttpClient Client, List<Installment> Installments)> SeedPolicyWithInstallmentsAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        AgencyContext.Current = fixture.AgencyAId;

        var thirdPartyLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();
        var customer = new Customer { AgencyId = fixture.AgencyAId, ExternalCode = $"EXT-{Guid.NewGuid():N}"[..12], FullName = "مشتری ویرایش قسط" };
        var vehicle = new Vehicle { AgencyId = fixture.AgencyAId, Plate = "66و666" };
        seedContext.Customers.Add(customer);
        seedContext.Vehicles.Add(vehicle);
        await seedContext.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var policy = new Policy
        {
            AgencyId = fixture.AgencyAId,
            PolicyNumber = $"POL-EDIT-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = thirdPartyLineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = today,
            StartDate = today,
            EndDate = today.AddYears(1),
            NetPremium = 2_000_000m,
            DownPayment = 0,
            InstallmentCount = 2,
        };
        seedContext.Policies.Add(policy);
        await seedContext.SaveChangesAsync();

        var installments = new List<Installment>
        {
            new() { AgencyId = fixture.AgencyAId, PolicyId = policy.Id, SeqNo = 1, DueDate = today, SettlementDeadline = today.AddDays(3), Amount = 1_000_000m, Status = InstallmentStatus.Unpaid },
            new() { AgencyId = fixture.AgencyAId, PolicyId = policy.Id, SeqNo = 2, DueDate = today.AddMonths(1), SettlementDeadline = today.AddMonths(1).AddDays(3), Amount = 1_000_000m, Status = InstallmentStatus.Unpaid },
        };
        seedContext.Installments.AddRange(installments);
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        return (client, installments);
    }
}
