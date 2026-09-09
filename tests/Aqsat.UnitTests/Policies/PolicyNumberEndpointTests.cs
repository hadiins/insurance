using System.Net;
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

/// <summary>docs/TASK-24-POLICY-NUMBER.md §2/§3/§6 — the issuance form's locked-segment display,
/// serial suggestion, and the one blocking rule (duplicate number in the same agency).</summary>
[Collection("WebApplicationFactory")]
public class PolicyNumberEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PolicyNumberEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Suggestion_lazily_seeds_parsian_defaults_and_composes_a_preview()
    {
        var (client, salisLineId, _) = await SeedAsync();

        var suggestion = await client.GetFromJsonAsync<PolicyNumberSuggestionDto>(
            $"/api/policies/number-suggestion?insuranceLineId={salisLineId}&issueDate=2026-01-01");

        Assert.NotNull(suggestion);
        Assert.Equal("1110", suggestion!.LineCode);
        Assert.True(suggestion.CanCompose);
        Assert.Equal("000001", suggestion.SuggestedSerial);
        Assert.Null(suggestion.LastSerial);
        Assert.NotNull(suggestion.ComposedPreview);
        Assert.EndsWith("/000001", suggestion.ComposedPreview);
    }

    [Fact]
    public async Task Suggestion_after_one_policy_proposes_the_next_serial_shared_across_lines()
    {
        var (client, salisLineId, atashLineId) = await SeedAsync();

        // Parsing splits on the separator only — it doesn't require the agency segment to match
        // Organization.AgencyCode (which is null until the first policy is issued, per §4.3), so a
        // fixed literal here is enough to prove the serial suggestion logic itself.
        // 2026-04-01 is Jalali 1405 (Nowruz 1405 falls ~2026-03-21) — matches the "405" year
        // segment below. IssueDate drives PnYear, not the system clock (§2's Esfand/Farvardin case).
        var issueDate = new DateOnly(2026, 4, 1);
        var createResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            "1110/576210/405/000247", salisLineId, null, "مشتری ثالث", null, null,
            Vehicle: new VehicleInput("۱۱الف۱۱۱", null, null, null, null, null), Property: null,
            issueDate, issueDate, issueDate.AddYears(1),
            9_000_000m, 500_000m, null, null, false));
        createResponse.EnsureSuccessStatusCode();

        // Third-party's serial 247 was just used — a fire policy issued next should be offered 248.
        var nextSuggestion = await client.GetFromJsonAsync<PolicyNumberSuggestionDto>(
            $"/api/policies/number-suggestion?insuranceLineId={atashLineId}&issueDate={issueDate:yyyy-MM-dd}");

        Assert.Equal("000248", nextSuggestion!.SuggestedSerial);
        Assert.Equal("000247", nextSuggestion.LastSerial);
    }

    [Fact]
    public async Task Duplicate_policy_number_in_the_same_agency_is_rejected_with_a_link_to_the_existing_policy()
    {
        var (client, salisLineId, _) = await SeedAsync();
        var number = $"1110/576210/405/{Guid.NewGuid():N}"[..30];

        var first = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            number, salisLineId, null, "مشتری اول", null, null,
            Vehicle: new VehicleInput("۱۱الف۱۱۱", null, null, null, null, null), Property: null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            9_000_000m, 500_000m, null, null, false));
        first.EnsureSuccessStatusCode();
        var firstResult = await first.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var second = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            number, salisLineId, null, "مشتری دوم", null, null,
            Vehicle: new VehicleInput("۲۲ب۲۲۲", null, null, null, null, null), Property: null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            9_000_000m, 500_000m, null, null, false));

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains(firstResult!.PolicyId.ToString(), problem!.Title);
    }

    [Fact]
    public async Task Manual_entry_escape_hatch_saves_unparsed_number_and_sets_the_flag()
    {
        var (client, salisLineId, _) = await SeedAsync();

        // Three segments, not four — the escape hatch's whole point is accepting a number the
        // active PolicyNumberFormat can't parse (§2's "تغییر فرمت توسط شرکت بیمه" case) without
        // rejecting the policy. Normalize() still legitimately strips stray whitespace from a
        // copy-pasted number (§5) — that isn't the thing under test here.
        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            "OLD-FORMAT/000042", salisLineId, null, "مشتری قدیمی", null, null,
            Vehicle: new VehicleInput("۱۱الف۱۱۱", null, null, null, null, null), Property: null,
            // Dates must stay inside the issuance guard's [today-1y, today+2y] window — the
            // "old" here is the legacy number format, not the issue date.
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            9_000_000m, 500_000m, null, null, false, PnManualEntry: true));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = (await client.GetFromJsonAsync<MeResponse>("/api/auth/me"))!.ActiveOrganizationId;
        var policy = await verify.Policies.AsNoTracking().SingleAsync(p => p.Id == result!.PolicyId);
        Assert.True(policy.PnManualEntry);
        Assert.False(policy.PnIsParsed);
        // §4.4 — PolicyNumber is the source of truth and is never rewritten, even though parsing
        // internally normalizes a copy of it (separators, digits) to attempt the 4-segment split.
        Assert.Equal("OLD-FORMAT/000042", policy.PolicyNumber);
    }

    [Fact]
    public async Task Number_warnings_flags_a_line_code_that_belongs_to_a_different_line()
    {
        var (client, _, atashLineId) = await SeedAsync();

        // Ensure the parsian line-code defaults exist (lazily seeded by a suggestion call), then ask
        // for atash's issuance to check a number carrying salis's own code ("1110").
        await client.GetFromJsonAsync<PolicyNumberSuggestionDto>(
            $"/api/policies/number-suggestion?insuranceLineId={atashLineId}&issueDate=2026-04-01");

        var warnings = await client.GetFromJsonAsync<PolicyNumberWarningsDto>(
            $"/api/policies/number-warnings?policyNumber=1110/576210/405/000001&insuranceLineId={atashLineId}&issueDate=2026-04-01");

        Assert.NotNull(warnings);
        Assert.Contains(warnings!.Warnings, w => w.Contains("1110"));
    }

    [Fact]
    public async Task Number_warnings_flags_an_agency_code_that_differs_from_the_configured_one()
    {
        var (client, salisLineId, _) = await SeedAsync();

        var warnings = await client.GetFromJsonAsync<PolicyNumberWarningsDto>(
            $"/api/policies/number-warnings?policyNumber=1110/999999/405/000001&insuranceLineId={salisLineId}&issueDate=2026-04-01");

        Assert.NotNull(warnings);
        Assert.Contains(warnings!.Warnings, w => w.Contains("نمایندگی"));
    }

    [Fact]
    public async Task Number_warnings_flags_a_year_that_differs_from_issue_date()
    {
        var (client, salisLineId, _) = await SeedAsync();

        // "405" normalizes to 1405, but 2026-01-01 is still Jalali 1404 (Nowruz 1405 is ~2026-03-21).
        var warnings = await client.GetFromJsonAsync<PolicyNumberWarningsDto>(
            $"/api/policies/number-warnings?policyNumber=1110/576210/405/000001&insuranceLineId={salisLineId}&issueDate=2026-01-01");

        Assert.NotNull(warnings);
        Assert.Contains(warnings!.Warnings, w => w.Contains("1405") && w.Contains("1404"));
    }

    [Fact]
    public async Task Number_warnings_is_empty_when_everything_matches()
    {
        var (client, salisLineId, _) = await SeedAsync();

        var warnings = await client.GetFromJsonAsync<PolicyNumberWarningsDto>(
            $"/api/policies/number-warnings?policyNumber=1110/576210/405/000001&insuranceLineId={salisLineId}&issueDate=2026-04-01");

        Assert.NotNull(warnings);
        Assert.Empty(warnings!.Warnings);
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

        // §4.3 — AgencyCode is configured once per agency (settings, out of this pass's scope) and
        // is a prerequisite for CanCompose; simulate an already-configured agency here.
        var organization = await seedContext.Organizations.FirstAsync(o => o.Id == fixture.AgencyAId);
        organization.AgencyCode = "576210";
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        return (client, salisLineId, atashLineId);
    }

    private sealed record ProblemDetailsDto(string Title);
}
