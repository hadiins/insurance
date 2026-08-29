using System.Net;
using System.Net.Http.Json;
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
        public int CallCount { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestPath = request.RequestUri!.AbsolutePath;
            var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(System.Text.Json.JsonDocument.Parse(jsonBody).RootElement) };
            return Task.FromResult(response);
        }
    }
}
