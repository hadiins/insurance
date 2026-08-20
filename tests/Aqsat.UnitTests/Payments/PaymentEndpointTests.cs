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
/// Task 10's own check (docs/TASKS.md): overpayment flows to the next installment, underpayment
/// stays partial and open, the identical payment submitted twice produces exactly one record, and a
/// reversal undoes the allocation with its own audit trail.
/// </summary>
[Collection("WebApplicationFactory")]
public class PaymentEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PaymentEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Overpayment_settles_the_hinted_installment_and_flows_the_remainder_to_the_next_one()
    {
        var (fixture, seedContext, client) = await SeedAsync();
        var (policy, installments) = await SeedPolicyWithInstallmentsAsync(seedContext, fixture.AgencyAId, [1_000_000m, 1_000_000m]);

        var response = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installments[0].Id, 1_500_000m, DateOnly.FromDateTime(DateTime.UtcNow), "Cash", null));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PaymentResultDto>();

        Assert.NotNull(result);
        Assert.Equal(2, result!.Allocations.Count);
        Assert.Equal(1_000_000m, result.Allocations.Single(a => a.InstallmentId == installments[0].Id).Amount);
        Assert.Equal(500_000m, result.Allocations.Single(a => a.InstallmentId == installments[1].Id).Amount);
        Assert.Equal(0m, result.UnallocatedAmount);

        AgencyContext.Current = fixture.AgencyAId;
        var first = await seedContext.Installments.AsNoTracking().SingleAsync(i => i.Id == installments[0].Id);
        var second = await seedContext.Installments.AsNoTracking().SingleAsync(i => i.Id == installments[1].Id);
        Assert.Equal(InstallmentStatus.Settled, first.Status);
        Assert.Equal(1_000_000m, first.PaidAmount);
        Assert.Equal(InstallmentStatus.Partial, second.Status);
        Assert.Equal(500_000m, second.PaidAmount);
        Assert.Equal(500_000m, second.Balance);
    }

    [Fact]
    public async Task Underpayment_is_partial_and_the_installment_stays_open()
    {
        var (fixture, seedContext, client) = await SeedAsync();
        var (policy, installments) = await SeedPolicyWithInstallmentsAsync(seedContext, fixture.AgencyAId, [1_000_000m]);

        var response = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installments[0].Id, 600_000m, DateOnly.FromDateTime(DateTime.UtcNow), "Cash", null));
        response.EnsureSuccessStatusCode();

        AgencyContext.Current = fixture.AgencyAId;
        var installment = await seedContext.Installments.AsNoTracking().SingleAsync(i => i.Id == installments[0].Id);
        Assert.Equal(InstallmentStatus.Partial, installment.Status);
        Assert.Equal(600_000m, installment.PaidAmount);
        Assert.Equal(400_000m, installment.Balance);
    }

    [Fact]
    public async Task Submitting_the_identical_payment_twice_produces_exactly_one_record()
    {
        var (fixture, seedContext, client) = await SeedAsync();
        var (policy, installments) = await SeedPolicyWithInstallmentsAsync(seedContext, fixture.AgencyAId, [1_000_000m]);
        var request = new RecordPaymentRequest(installments[0].Id, 400_000m, DateOnly.FromDateTime(DateTime.UtcNow), "Cash", null);

        var firstResponse = await client.PostAsJsonAsync("/api/payments", request);
        firstResponse.EnsureSuccessStatusCode();
        var firstResult = await firstResponse.Content.ReadFromJsonAsync<PaymentResultDto>();

        var secondResponse = await client.PostAsJsonAsync("/api/payments", request);
        secondResponse.EnsureSuccessStatusCode();
        var secondResult = await secondResponse.Content.ReadFromJsonAsync<PaymentResultDto>();

        Assert.Equal(firstResult!.PaymentId, secondResult!.PaymentId);

        AgencyContext.Current = fixture.AgencyAId;
        var paymentCount = await seedContext.Payments.AsNoTracking()
            .CountAsync(p => p.InstallmentIdHint == installments[0].Id);
        Assert.Equal(1, paymentCount);

        var installment = await seedContext.Installments.AsNoTracking().SingleAsync(i => i.Id == installments[0].Id);
        Assert.Equal(400_000m, installment.PaidAmount);
    }

    [Fact]
    public async Task Reversing_a_payment_undoes_the_allocation_and_writes_an_audit_row()
    {
        var (fixture, seedContext, client) = await SeedAsync();
        var (policy, installments) = await SeedPolicyWithInstallmentsAsync(seedContext, fixture.AgencyAId, [1_000_000m]);

        var recordResponse = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installments[0].Id, 1_000_000m, DateOnly.FromDateTime(DateTime.UtcNow), "Cash", null));
        recordResponse.EnsureSuccessStatusCode();
        var recorded = await recordResponse.Content.ReadFromJsonAsync<PaymentResultDto>();

        var reverseResponse = await client.PostAsync($"/api/payments/{recorded!.PaymentId}/reverse", null);
        Assert.Equal(HttpStatusCode.NoContent, reverseResponse.StatusCode);

        AgencyContext.Current = fixture.AgencyAId;
        var installment = await seedContext.Installments.AsNoTracking().SingleAsync(i => i.Id == installments[0].Id);
        Assert.Equal(InstallmentStatus.Unpaid, installment.Status);
        Assert.Equal(0m, installment.PaidAmount);

        var reversalAudit = await seedContext.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.EntityId == recorded.PaymentId && a.Action == AuditAction.PaymentReversed);
        Assert.Equal(policy.Id, reversalAudit.PolicyId);
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

    private static async Task<(Policy Policy, List<Installment> Installments)> SeedPolicyWithInstallmentsAsync(
        AppDbContext seedContext, Guid agencyId, IReadOnlyList<decimal> installmentAmounts)
    {
        AgencyContext.Current = agencyId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thirdPartyLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var customer = new Customer { AgencyId = agencyId, ExternalCode = $"EXT-{Guid.NewGuid():N}"[..12], FullName = "مشتری پرداخت" };
        var vehicle = new Vehicle { AgencyId = agencyId, Plate = "44د444" };
        seedContext.Customers.Add(customer);
        seedContext.Vehicles.Add(vehicle);
        await seedContext.SaveChangesAsync();

        var policy = new Policy
        {
            AgencyId = agencyId,
            PolicyNumber = $"POL-PAY-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = thirdPartyLineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = today,
            StartDate = today,
            EndDate = today.AddYears(1),
            NetPremium = installmentAmounts.Sum(),
            DownPayment = 0,
            InstallmentCount = installmentAmounts.Count,
        };
        seedContext.Policies.Add(policy);
        await seedContext.SaveChangesAsync();

        var installments = installmentAmounts
            .Select((amount, index) => new Installment
            {
                AgencyId = agencyId,
                PolicyId = policy.Id,
                SeqNo = index + 1,
                DueDate = today.AddMonths(index),
                SettlementDeadline = today.AddMonths(index).AddDays(3),
                Amount = amount,
                Status = InstallmentStatus.Unpaid,
            })
            .ToList();
        seedContext.Installments.AddRange(installments);
        await seedContext.SaveChangesAsync();

        return (policy, installments);
    }
}
