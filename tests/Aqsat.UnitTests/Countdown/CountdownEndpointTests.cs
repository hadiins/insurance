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
using Microsoft.Extensions.DependencyInjection;

namespace Aqsat.UnitTests.Countdown;

/// <summary>
/// Task 9's own check (docs/TASKS.md): seed installments at every urgency level, confirm the
/// dashboard orders them correctly and the shortfall figure is arithmetically right, then move the
/// system clock forward one day and confirm everything re-sorts (windows shift, urgency relabels).
/// </summary>
[Collection("WebApplicationFactory")]
public class CountdownEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Today = new(2026, 3, 10);
    private readonly MutableTimeProvider _timeProvider = new(new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
    private readonly WebApplicationFactory<Program> _factory;

    public CountdownEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(_timeProvider));
        });
    }

    [Fact]
    public async Task Dashboard_orders_rows_by_deadline_then_amount_and_the_shortfall_is_arithmetically_right()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        AgencyContext.Current = fixture.AgencyAId;

        var thirdPartyLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();
        var customer = new Customer { AgencyId = fixture.AgencyAId, ExternalCode = "EXT-CD", FullName = "مشتری شمارش‌معکوس" };
        var vehicle = new Vehicle { AgencyId = fixture.AgencyAId, Plate = "22ب222" };
        seedContext.Customers.Add(customer);
        seedContext.Vehicles.Add(vehicle);
        await seedContext.SaveChangesAsync();

        var policy = new Policy
        {
            AgencyId = fixture.AgencyAId,
            PolicyNumber = $"POL-CD-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = thirdPartyLineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = Today,
            StartDate = Today,
            EndDate = Today.AddYears(1),
            NetPremium = 20_000_000,
            DownPayment = 0,
            InstallmentCount = 0,
        };
        seedContext.Policies.Add(policy);
        await seedContext.SaveChangesAsync();

        // Overdue: deadline already passed.
        var overdue = MakeInstallment(fixture.AgencyAId, policy.Id, 1, Today.AddDays(-10), Today.AddDays(-1), 1_000_000);
        // Critical, zero days left — the earliest deadline of the set, so it must sort first.
        var critical = MakeInstallment(fixture.AgencyAId, policy.Id, 2, Today.AddDays(-3), Today, 500_000);
        critical.PaidAmount = 200_000;
        // Same deadline as `critical` — amount DESC must place this ahead of it.
        var criticalSameDeadlineLargerAmount = MakeInstallment(fixture.AgencyAId, policy.Id, 3, Today.AddDays(-3), Today, 1_500_000);
        // Warning, three days left.
        var warning = MakeInstallment(fixture.AgencyAId, policy.Id, 4, Today.AddDays(-1), Today.AddDays(3), 2_000_000);
        // Upcoming — due within 7 days, window not open yet.
        var upcoming = MakeInstallment(fixture.AgencyAId, policy.Id, 5, Today.AddDays(5), Today.AddDays(8), 700_000);
        // Outside the query's 30-day settled-deadline floor — must never appear.
        var tooOld = MakeInstallment(fixture.AgencyAId, policy.Id, 6, Today.AddDays(-45), Today.AddDays(-40), 300_000);
        // Sits right past the +7 due-date ceiling on day 0 — proves the window shifts, not just labels.
        var justOutsideWindow = MakeInstallment(fixture.AgencyAId, policy.Id, 7, Today.AddDays(8), Today.AddDays(11), 999_999);
        // Already settled — must never appear regardless of dates.
        var settled = MakeInstallment(fixture.AgencyAId, policy.Id, 8, Today.AddDays(-20), Today.AddDays(-17), 400_000);
        settled.Status = InstallmentStatus.Settled;
        settled.PaidAmount = 400_000;

        seedContext.Installments.AddRange(
            overdue, critical, criticalSameDeadlineLargerAmount, warning, upcoming, tooOld, justOutsideWindow, settled);
        await seedContext.SaveChangesAsync();

        var client = await CreateAuthenticatedClientAsync(fixture);

        var day0 = await client.GetFromJsonAsync<CountdownDashboardDto>("/api/countdown");
        Assert.NotNull(day0);

        var ids = day0!.Rows.Select(r => r.InstallmentId).ToList();
        Assert.DoesNotContain(tooOld.Id, ids);
        Assert.DoesNotContain(justOutsideWindow.Id, ids);
        Assert.DoesNotContain(settled.Id, ids);

        // Deadline ASC first (overdue's deadline is the earliest of the set), Amount DESC as the
        // tiebreak between the two rows sharing `critical`'s deadline (docs/PHASE-1-SPEC.md §3.3).
        Assert.Equal(
            [overdue.Id, criticalSameDeadlineLargerAmount.Id, critical.Id, warning.Id, upcoming.Id],
            ids);

        Assert.Equal("Overdue", day0.Rows.Single(r => r.InstallmentId == overdue.Id).Urgency);
        Assert.Equal("Critical", day0.Rows.Single(r => r.InstallmentId == critical.Id).Urgency);
        Assert.Equal("Warning", day0.Rows.Single(r => r.InstallmentId == warning.Id).Urgency);
        Assert.Equal("Upcoming", day0.Rows.Single(r => r.InstallmentId == upcoming.Id).Urgency);

        var expectedOwed = day0.Rows.Sum(r => r.Amount);
        var expectedCollected = day0.Rows.Sum(r => r.PaidAmount);
        Assert.Equal(expectedOwed, day0.Owed);
        Assert.Equal(expectedCollected, day0.Collected);
        Assert.Equal(expectedOwed - expectedCollected, day0.Shortfall);
        Assert.Equal(200_000m, day0.Rows.Single(r => r.InstallmentId == critical.Id).PaidAmount);

        // Move the clock forward one day: the +7 window shifts to admit justOutsideWindow, and the
        // zero-day-left `critical` row's deadline is now in the past.
        _timeProvider.Advance(TimeSpan.FromDays(1));

        var day1 = await client.GetFromJsonAsync<CountdownDashboardDto>("/api/countdown");
        Assert.NotNull(day1);
        var day1Ids = day1!.Rows.Select(r => r.InstallmentId).ToList();

        Assert.Contains(justOutsideWindow.Id, day1Ids);
        Assert.Equal("Overdue", day1.Rows.Single(r => r.InstallmentId == critical.Id).Urgency);
    }

    private static Installment MakeInstallment(
        Guid agencyId, Guid policyId, int seqNo, DateOnly dueDate, DateOnly settlementDeadline, decimal amount) =>
        new()
        {
            AgencyId = agencyId,
            PolicyId = policyId,
            SeqNo = seqNo,
            DueDate = dueDate,
            SettlementDeadline = settlementDeadline,
            Amount = amount,
            Status = InstallmentStatus.Unpaid,
        };

    private async Task<HttpClient> CreateAuthenticatedClientAsync(DevSeeder.SeededAuthFixture fixture)
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());
        return client;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
