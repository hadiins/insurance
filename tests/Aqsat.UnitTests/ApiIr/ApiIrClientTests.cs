using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aqsat.Application.ApiIr;
using Aqsat.Domain;
using Aqsat.Infrastructure.ApiIr;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Aqsat.UnitTests.ApiIr;

/// <summary>
/// docs/TASKS.md Task 14: sandbox-by-default routing, success-checked (not data-checked) envelopes,
/// and cost logging — without ever making a real network call.
/// </summary>
public class ApiIrClientTests
{
    [Fact]
    public async Task With_paid_endpoints_disallowed_a_paid_call_is_routed_to_sandbox_echo_and_logged_as_sandboxed()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;
        await ClearApiIrSettingsAsync(context);

        var handler = new RecordingHandler(HttpStatusCode.OK, """{"echo":true}""");
        var client = BuildClient(context, handler, allowPaidEndpoints: false);

        var sent = await client.SendSmsAsync("09121234567", "متن آزمایشی", agencyA.AgencyId);

        Assert.True(sent);
        Assert.Equal("/api/Sandbox/Echo", handler.LastRequestPath);

        var log = await context.ApiIrCallLogs.AsNoTracking().SingleAsync(l => l.Service == "SendSms");
        Assert.True(log.WasSandboxed);
        Assert.True(log.Success);
        Assert.Equal(115m, log.CostToman);
    }

    [Fact]
    public async Task With_paid_endpoints_allowed_the_real_endpoint_is_called_and_envelope_success_is_checked_not_just_data_presence()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;
        await ClearApiIrSettingsAsync(context);

        // api.ir's own docs warn success:false can appear alongside populated data — a client that
        // only checks for Data presence would wrongly treat this as success (CLAUDE.md rule 18).
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"data":{"isMatched":true},"success":false,"code":400,"message":"nope"}""");
        var client = BuildClient(context, handler, allowPaidEndpoints: true);

        var result = await client.ShahkarLiteAsync("0072345453", "09121234567", agencyA.AgencyId);

        Assert.Null(result);
        Assert.Equal("/api/sw1/ShahkarLite", handler.LastRequestPath);

        var log = await context.ApiIrCallLogs.AsNoTracking().SingleAsync(l => l.Service == "ShahkarLite");
        Assert.False(log.Success);
        Assert.False(log.WasSandboxed);
    }

    [Fact]
    public async Task A_successful_envelope_is_returned_and_cached()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;
        await ClearApiIrSettingsAsync(context);

        var handler = new RecordingHandler(HttpStatusCode.OK, """{"data":{"isMatched":true},"success":true,"code":200,"message":null}""");
        var client = BuildClient(context, handler, allowPaidEndpoints: true);

        var first = await client.ShahkarLiteAsync("0072345453", "09121234567", agencyA.AgencyId);
        Assert.NotNull(first);
        Assert.True(first!.Value.Matched);

        handler.CallCount = 0;
        var second = await client.ShahkarLiteAsync("0072345453", "09121234567", agencyA.AgencyId);
        Assert.True(second!.Value.Matched);
        Assert.Equal(0, handler.CallCount); // cached forever — no second HTTP call.
    }

    [Fact]
    public async Task SendSms_posts_mobiles_array_with_a_message_field_not_a_bare_mobile_string()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;
        await ClearApiIrSettingsAsync(context);

        var handler = new RecordingHandler(HttpStatusCode.OK, """{"data":731243,"success":true,"code":200,"message":null}""");
        var client = BuildClient(context, handler, allowPaidEndpoints: true);

        var sent = await client.SendSmsAsync("09121234567", "متن آزمایشی", agencyA.AgencyId);

        Assert.True(sent);
        Assert.Equal("/api/sw1/SendSms", handler.LastRequestPath);

        var body = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        Assert.Equal("متن آزمایشی", body.GetProperty("message").GetString());
        Assert.Equal(1, body.GetProperty("mobiles").GetArrayLength());
        Assert.Equal("09121234567", body.GetProperty("mobiles")[0].GetString());
        Assert.False(body.TryGetProperty("mobile", out _)); // api.ir ignores this spelling server-side.
    }

    [Fact]
    public async Task A_numeric_data_field_does_not_break_a_successful_send()
    {
        // api.ir's SendSms answers with `data` as the bare numeric message id — a rigid
        // ApiIrEnvelope<SendResponse> binding would throw on that and report a false failure.
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;
        await ClearApiIrSettingsAsync(context);

        var handler = new RecordingHandler(HttpStatusCode.OK, """{"data":731243,"success":true,"code":200,"message":null}""");
        var client = BuildClient(context, handler, allowPaidEndpoints: true);

        var sent = await client.SendSmsAsync("09121234567", "متن آزمایشی", agencyA.AgencyId);

        Assert.True(sent);
    }

    [Fact]
    public async Task An_apiir_settings_row_overrides_the_configured_key_and_paid_flag()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;

        await ClearApiIrSettingsAsync(context);
        context.ApiIrSettings.Add(new ApiIrSettings { ApiKey = "row-key", AllowPaidEndpoints = true });
        await context.SaveChangesAsync();

        // Config says sandbox + test-key; the DB row says real endpoint + row-key — the row wins.
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"data":{"isMatched":true},"success":true,"code":200,"message":null}""");
        var client = BuildClient(context, handler, allowPaidEndpoints: false);

        var result = await client.ShahkarLiteAsync("0072345453", "09121234567", agencyA.AgencyId);

        Assert.True(result!.Value.Matched);
        Assert.Equal("/api/sw1/ShahkarLite", handler.LastRequestPath);
        Assert.Equal("Bearer row-key", handler.LastAuthHeader);
    }

    [Fact]
    public async Task An_apiir_settings_row_can_force_sandbox_even_when_config_allows_paid_endpoints()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;

        await ClearApiIrSettingsAsync(context);
        context.ApiIrSettings.Add(new ApiIrSettings { ApiKey = "", AllowPaidEndpoints = false });
        await context.SaveChangesAsync();

        var handler = new RecordingHandler(HttpStatusCode.OK, """{"echo":true}""");
        var client = BuildClient(context, handler, allowPaidEndpoints: true);

        var sent = await client.SendSmsAsync("09121234567", "متن آزمایشی", agencyA.AgencyId);

        Assert.True(sent);
        Assert.Equal("/api/Sandbox/Echo", handler.LastRequestPath); // the row's explicit false wins.
    }

    [Fact]
    public async Task UnpaidCheque_and_ActiveLoans_route_to_sandbox_when_paid_endpoints_are_disallowed()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;
        await ClearApiIrSettingsAsync(context);

        var handler = new RecordingHandler(HttpStatusCode.OK, """{"echo":true}""");
        var client = BuildClient(context, handler, allowPaidEndpoints: false);

        var cheque = await client.UnpaidChequeAsync("0072345453", agencyA.AgencyId);
        var loans = await client.ActiveLoansAsync("0072345453", agencyA.AgencyId);

        // Sandboxed ≠ failed: a non-null result with null fields means "no real answer", so the
        // verification chain can still show the representative RawSuccess=false instead of zeros
        // presented as facts or a hard failure for a fee the customer already paid.
        Assert.NotNull(cheque);
        Assert.Null(cheque!.Count);
        Assert.Null(cheque.SumAmountRial);
        Assert.NotNull(loans);
        Assert.Null(loans!.Count);
        Assert.Null(loans.TotalAmountRial);
        Assert.Equal("/api/Sandbox/Echo", handler.LastRequestPath);
        // EchoReq rejects a body without its required `name` field — without it the real
        // api.ir sandbox answers 400 "Name required" and every sandboxed call looks broken.
        Assert.Contains("\"name\"", handler.LastRequestBody);
        Assert.Contains("\"nationalCode\"", handler.LastRequestBody);

        var logs = await context.ApiIrCallLogs.AsNoTracking()
            .Where(l => l.Service == "UnpaidCheque" || l.Service == "ActiveLoans").ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l =>
        {
            Assert.True(l.WasSandboxed);
            Assert.True(l.Success);
            Assert.Equal(1_100m, l.CostToman);
        });
    }

    [Fact]
    public async Task UnpaidCheque_with_success_false_is_rejected_not_silently_zeroed()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;
        await ClearApiIrSettingsAsync(context);

        // rule 18 — api.ir pairs success:false with populated data; data presence is not success.
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """{"data":{"count":2,"sumAmount":1000000,"sumBouncedAmount":500000},"success":false,"code":400,"message":"nope"}""");
        var client = BuildClient(context, handler, allowPaidEndpoints: true);

        var result = await client.UnpaidChequeAsync("0072345453", agencyA.AgencyId);

        Assert.Null(result);
        Assert.Equal("/api/sw1/UnpaidCheque", handler.LastRequestPath);
        var log = await context.ApiIrCallLogs.AsNoTracking().SingleAsync(l => l.Service == "UnpaidCheque");
        Assert.False(log.Success);
        Assert.False(log.WasSandboxed);
    }

    [Fact]
    public async Task ActiveLoans_parses_rial_amounts_delivered_as_json_strings()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;
        await ClearApiIrSettingsAsync(context);

        // The OpenAPI spec types the loan amounts integer|string — api.ir actually sends strings.
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """{"data":{"count":3,"info":{"nationalCode":"0072345453","name":"name","totalAmount":"1200000000","debtTotalAmount":"300000000","pastExpiredTotalAmount":"0","deferredTotalAmount":"0","suspiciousTotalAmount":"0","dishonored":"0"}},"success":true,"code":200,"message":null}""");
        var client = BuildClient(context, handler, allowPaidEndpoints: true);

        var result = await client.ActiveLoansAsync("0072345453", agencyA.AgencyId);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Count);
        Assert.Equal(1_200_000_000m, result.TotalAmountRial);
        Assert.Equal(300_000_000m, result.DebtTotalAmountRial);
        Assert.Equal(0m, result.PastExpiredTotalAmountRial);
        Assert.Equal(0m, result.DishonoredRial);
    }

    [Fact]
    public async Task ActiveLoans_with_null_info_still_returns_the_real_count()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;
        await ClearApiIrSettingsAsync(context);

        // api.ir answers info:null for a person with no active facilities — the count is still real
        // and must not be flattened into "sandboxed/no answer".
        var handler = new RecordingHandler(
            HttpStatusCode.OK, """{"data":{"count":0,"info":null},"success":true,"code":200,"message":null}""");
        var client = BuildClient(context, handler, allowPaidEndpoints: true);

        var result = await client.ActiveLoansAsync("0072345453", agencyA.AgencyId);

        Assert.NotNull(result);
        Assert.Equal(0, result!.Count);
        Assert.Null(result.TotalAmountRial);
    }

    // Shared LocalDB: every test here routes by resolved settings, so a singleton ApiIrSettings row
    // left behind by a sibling test (or a live panel edit against the dev DB) would silently flip
    // sandbox/real routing. Each test drops the table's rows before seeding its own state.
    private static async Task ClearApiIrSettingsAsync(AppDbContext context) =>
        await context.Database.ExecuteSqlRawAsync("DELETE FROM ApiIrSettings");

    private static ApiIrClient BuildClient(AppDbContext context, HttpMessageHandler handler, bool allowPaidEndpoints)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://s.api.ir") };
        var options = Options.Create(new ApiIrOptions { BaseUrl = "https://s.api.ir", ApiKey = "test-key", AllowPaidEndpoints = allowPaidEndpoints });
        return new ApiIrClient(httpClient, context, new MemoryCache(new MemoryCacheOptions()), options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ApiIrClient>.Instance);
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string jsonBody) : HttpMessageHandler
    {
        public string? LastRequestPath { get; private set; }
        public string? LastRequestBody { get; private set; }
        public string? LastAuthHeader { get; private set; }
        public int CallCount { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestPath = request.RequestUri!.AbsolutePath;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            LastAuthHeader = request.Headers.Authorization?.ToString();
            var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(JsonDocument.Parse(jsonBody).RootElement) };
            return response;
        }
    }
}
