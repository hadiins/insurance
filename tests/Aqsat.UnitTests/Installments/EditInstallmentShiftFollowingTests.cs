using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Schedule;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Installments;

/// <summary>PUT /api/installments/{id} with ShiftFollowing — the owner's request that editing one
/// installment's due date re-lays the remaining installments on the same Jalali monthly cadence,
/// anchored to the new date. Settled installments are history and never move (CLAUDE.md).</summary>
[Collection("WebApplicationFactory")]
public class EditInstallmentShiftFollowingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly DevSeeder.SeededAuthFixture _fixture;

    public EditInstallmentShiftFollowingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var seedContext = TestDbContextFactory.Create();
        _fixture = DevSeeder.SeedAuthFixtureAsync(seedContext).GetAwaiter().GetResult();
        InsuranceLineSeeder.EnsureSeededAsync(seedContext).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> LoginAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(_fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", _fixture.AgencyAId.ToString());
        return client;
    }

    private async Task<(Guid PolicyId, AppDbContext SeedContext)> IssueScheduledPolicyAsync(HttpClient client, int installmentCount)
    {
        var lineId = GetLineId();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"SHF-{Guid.NewGuid():N}"[..20], lineId, null, $"مشتری {Guid.NewGuid():N}"[..12], null, null,
            new VehicleInput(null, null, null, null, null, null,
                PlateTwoDigit: "12", PlateLetter: "ب", PlateThreeDigit: "345", PlateIranCode: "22"),
            Property: null,
            today, today, today.AddYears(1), 1_000_000m * installmentCount, 0m, null, null, false));
        response.EnsureSuccessStatusCode();
        var policy = (await response.Content.ReadFromJsonAsync<CreatePolicyResultDto>())!;

        var schedule = await client.PostAsJsonAsync($"/api/policies/{policy.PolicyId}/schedule",
            new ScheduleRequest(0m, installmentCount));
        schedule.EnsureSuccessStatusCode();

        return (policy.PolicyId, TestDbContextFactory.Create());
    }

    private static async Task<List<Installment>> LoadAsync(AppDbContext seedContext, Guid policyId)
    {
        return await seedContext.Installments.AsNoTracking()
            .Where(i => i.PolicyId == policyId).OrderBy(i => i.SeqNo).ToListAsync();
    }

    private static Guid GetLineId()
    {
        using var seedContext = TestDbContextFactory.Create();
        return seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).First();
    }

    [Fact]
    public async Task ShiftFollowing_relays_remaining_installments_but_never_settled_ones()
    {
        var client = await LoginAsync();
        var (policyId, seedContext) = await IssueScheduledPolicyAsync(client, 4);
        await using (seedContext)
        {
            AgencyContext.Current = _fixture.AgencyAId;
            var original = await LoadAsync(seedContext, policyId);
            Assert.Equal(4, original.Count);

            // Settle installment 3 — it must stay frozen through the re-lay below. The allocation
            // is explicit: without it PaymentAllocator spreads by due date from the earliest open
            // installment (§3.4), which would settle seq 1 instead of seq 3.
            var settledDue = original[2].DueDate;
            var payment = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
                original[2].Id, original[2].Amount, DateOnly.FromDateTime(DateTime.UtcNow), "Cash", null,
                Allocations: [new AllocationLineRequest(original[2].Id, original[2].Amount)]));
            payment.EnsureSuccessStatusCode();

            // Edit installment 2 onto a new date, asking the rest to follow.
            var newDue = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(45);
            var edit = await client.PutAsJsonAsync($"/api/installments/{original[1].Id}",
                new UpdateInstallmentRequest(null, newDue, true));
            edit.EnsureSuccessStatusCode();

            var after = await LoadAsync(seedContext, policyId);
            Assert.Equal(original[0].DueDate, after[0].DueDate);
            Assert.Equal(newDue, after[1].DueDate);
            Assert.Equal(settledDue, after[2].DueDate);
            Assert.Equal(DueDateCalculator.AddPersianMonths(newDue, 2), after[3].DueDate);

            // The moved installment's deadline was recomputed from its new due date (holiday shift
            // is bounded to a few days past the plain +3).
            Assert.InRange(after[3].SettlementDeadline, after[3].DueDate.AddDays(3), after[3].DueDate.AddDays(10));
            Assert.True(after[3].IsManuallyEdited);
            Assert.True(after[1].IsManuallyEdited);
            Assert.False(after[0].IsManuallyEdited);
        }
    }

    [Fact]
    public async Task Without_shift_following_only_the_edited_installment_moves()
    {
        var client = await LoginAsync();
        var (policyId, seedContext) = await IssueScheduledPolicyAsync(client, 3);
        await using (seedContext)
        {
            AgencyContext.Current = _fixture.AgencyAId;
            var original = await LoadAsync(seedContext, policyId);
            var newDue = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(60);

            var edit = await client.PutAsJsonAsync($"/api/installments/{original[0].Id}",
                new UpdateInstallmentRequest(null, newDue, false));
            edit.EnsureSuccessStatusCode();

            var after = await LoadAsync(seedContext, policyId);
            Assert.Equal(newDue, after[0].DueDate);
            Assert.Equal(original[1].DueDate, after[1].DueDate);
            Assert.Equal(original[2].DueDate, after[2].DueDate);
            Assert.False(after[1].IsManuallyEdited);
            Assert.False(after[2].IsManuallyEdited);
        }
    }
}
