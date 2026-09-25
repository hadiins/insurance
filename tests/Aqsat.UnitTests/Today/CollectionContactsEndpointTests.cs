using Aqsat.Application.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Today;

/// <summary>
/// The collection log behind «آخرین تماس» and «قول پرداخت». Two things are load-bearing here and
/// both are asserted: the audit row is written in the same transaction as the contact (rules 28/29),
/// and a policy outside the caller's agency is refused rather than silently accepted through the
/// foreign key (rule 11).
/// </summary>
[Collection("WebApplicationFactory")]
public class CollectionContactsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CollectionContactsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task A_logged_promise_is_stored_audited_and_readable_back_with_the_policy()
    {
        var (client, _, policy, installmentId) = await SetUpPolicyAsync();
        var promisedOn = IranClock.Today().AddDays(3);

        var response = await client.PostAsJsonAsync("/api/collection-contacts", new CreateCollectionContactRequest(
            policy.PolicyId, installmentId, "Call", "Promised", DateTimeOffset.UtcNow, promisedOn, 750_000m));
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CollectionContactDto>())!;
        Assert.Equal("Promised", created.Outcome);
        Assert.Equal(750_000m, created.PromisedAmount);
        Assert.Equal(installmentId, created.InstallmentId);

        var history = await client.GetFromJsonAsync<List<CollectionContactDto>>(
            $"/api/collection-contacts?policyId={policy.PolicyId}");
        Assert.Contains(history!, c => c.Id == created.Id);

        // Rules 28/29: the audit row exists, carries the PolicyId even though the contact names an
        // installment, and describes the action rather than the person. Its action is Created —
        // CollectionContact IS the logged event, so the audit override writes it automatically
        // rather than leaving a developer to remember; EntityType is what tells it apart.
        await using var seedContext = TestDbContextFactory.Create();
        var agencyId = Guid.Parse(client.DefaultRequestHeaders.GetValues("X-Organization-Id").Single());
        AgencyContext.Current = agencyId;
        var audit = await seedContext.AuditEntries
            .Where(a => a.EntityId == created.Id)
            .ToListAsync();
        var entry = Assert.Single(audit);
        Assert.Equal(AuditAction.Created, entry.Action);
        Assert.Equal(policy.PolicyId, entry.PolicyId);
        Assert.Equal(nameof(Aqsat.Domain.CollectionContact), entry.EntityType);
    }

    [Fact]
    public async Task Promise_fields_are_accepted_exactly_when_the_outcome_is_a_promise()
    {
        var (client, _, policy, _) = await SetUpPolicyAsync();
        var today = IranClock.Today();

        // A promise with no date is not a promise — the outcome says one thing and the row another.
        var noDate = await client.PostAsJsonAsync("/api/collection-contacts", new CreateCollectionContactRequest(
            policy.PolicyId, null, "Call", "Promised", DateTimeOffset.UtcNow, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, noDate.StatusCode);

        // And the mirror: an answer that is not a promise cannot carry promise terms.
        var notAPromise = await client.PostAsJsonAsync("/api/collection-contacts", new CreateCollectionContactRequest(
            policy.PolicyId, null, "Call", "NoAnswer", DateTimeOffset.UtcNow, today.AddDays(1), 100_000m));
        Assert.Equal(HttpStatusCode.BadRequest, notAPromise.StatusCode);

        var negative = await client.PostAsJsonAsync("/api/collection-contacts", new CreateCollectionContactRequest(
            policy.PolicyId, null, "Call", "Promised", DateTimeOffset.UtcNow, today.AddDays(1), -5_000m));
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);

        // A call logged tomorrow did not happen.
        var inTheFuture = await client.PostAsJsonAsync("/api/collection-contacts", new CreateCollectionContactRequest(
            policy.PolicyId, null, "Call", "NoAnswer", DateTimeOffset.UtcNow.AddDays(1), null, null));
        Assert.Equal(HttpStatusCode.BadRequest, inTheFuture.StatusCode);

        var names = await client.PostAsJsonAsync("/api/collection-contacts", new CreateCollectionContactRequest(
            policy.PolicyId, null, "Call", "چیزی", DateTimeOffset.UtcNow, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, names.StatusCode);
    }

    [Fact]
    public async Task A_promise_whose_date_has_passed_stops_matching_the_promise_chip()
    {
        var (client, _, policy, _) = await SetUpPolicyAsync();
        var today = IranClock.Today();

        // «شکسته» is derived, never stored: the same row that matched yesterday stops matching
        // today, with nothing having to run to change its state.
        var past = await client.PostAsJsonAsync("/api/collection-contacts", new CreateCollectionContactRequest(
            policy.PolicyId, null, "Call", "Promised", DateTimeOffset.UtcNow.AddDays(-5), today.AddDays(-2), 200_000m));
        past.EnsureSuccessStatusCode();
        Assert.DoesNotContain(
            (await PromisedPolicyIdsAsync(client)).ToList(), id => id == policy.PolicyId);

        // A promise that supersedes it and is still in the future puts the policy back on the list.
        var live = await client.PostAsJsonAsync("/api/collection-contacts", new CreateCollectionContactRequest(
            policy.PolicyId, null, "Call", "Promised", DateTimeOffset.UtcNow, today.AddDays(4), 200_000m));
        live.EnsureSuccessStatusCode();
        Assert.Contains(policy.PolicyId, await PromisedPolicyIdsAsync(client));
    }

    [Fact]
    public async Task Another_agencys_policy_is_refused_rather_than_silently_accepted()
    {
        var (client, _, policy, installmentId) = await SetUpPolicyAsync();

        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await SignInFixtureAsync(seedContext);
        var clientB = await SignInAsAsync(fixture.DualAgencyManagerMobile, fixture.AgencyBId);

        // Rule 11: RLS hides agency A's policy from agency B, but a foreign key to an invisible row
        // succeeds silently — so the write must be refused, not accepted.
        var write = await clientB.PostAsJsonAsync("/api/collection-contacts", new CreateCollectionContactRequest(
            policy.PolicyId, installmentId, "Call", "NoAnswer", DateTimeOffset.UtcNow, null, null));
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);

        // And reading its history is a 404, not an empty list — "no access" and "no contacts" must
        // never look the same (rule 17).
        var read = await clientB.GetAsync($"/api/collection-contacts?policyId={policy.PolicyId}");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        // The contact agency A logged in the meantime stays invisible to agency B.
        var mine = await client.PostAsJsonAsync("/api/collection-contacts", new CreateCollectionContactRequest(
            policy.PolicyId, null, "Call", "NoAnswer", DateTimeOffset.UtcNow, null, null));
        mine.EnsureSuccessStatusCode();

        var unscoped = await clientB.GetAsync("/api/collection-contacts");
        Assert.Equal(HttpStatusCode.BadRequest, unscoped.StatusCode);
    }

    private static async Task<IReadOnlyList<Guid>> PromisedPolicyIdsAsync(HttpClient client)
    {
        var rows = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>("/api/installments?filter=promise");
        return rows!.Select(r => r.PolicyId).Distinct().ToList();
    }

    private async Task<(HttpClient Client, DevSeeder.SeededAuthFixture Fixture, CreatePolicyResultDto Policy, Guid InstallmentId)>
        SetUpPolicyAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await SignInFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var lineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var client = await SignInAsAsync(fixture.DualAgencyManagerMobile, fixture.AgencyAId);

        var today = IranClock.Today();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-CC-{unique}", lineId, null, $"مشتری تماس {unique}", null, null,
            Vehicle: new VehicleInput($"۶۶د{unique[..3]}", null, null, null, null, null), Property: null,
            today.AddMonths(-2), today.AddMonths(-2), today.AddYears(1), 9_000_000m, 0m, null, null, false));
        response.EnsureSuccessStatusCode();
        var policy = (await response.Content.ReadFromJsonAsync<CreatePolicyResultDto>())!;

        var schedule = await client.PostAsJsonAsync(
            $"/api/policies/{policy.PolicyId}/schedule", new ScheduleRequest(0m, 3));
        schedule.EnsureSuccessStatusCode();

        var rows = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>(
            $"/api/installments?search={policy.PolicyNumber}");
        var installmentId = rows!.First(r => r.PolicyId == policy.PolicyId).InstallmentId;

        return (client, fixture, policy, installmentId);
    }

    private static Task<DevSeeder.SeededAuthFixture> SignInFixtureAsync(AppDbContext context) =>
        DevSeeder.SeedAuthFixtureAsync(context);

    private async Task<HttpClient> SignInAsAsync(string mobile, Guid agencyId)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, DevSeeder.SeededUserPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", agencyId.ToString());
        return client;
    }
}
