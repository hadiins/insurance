using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Aqsat.Infrastructure.Monitoring;

/// <summary>
/// Times every request and feeds the RequestMetricsAggregator — the APM panel's raw data. Runs
/// before the exception handler's terminal short-circuit is even possible... it wraps everything
/// after it, so a 500 from the GlobalExceptionHandler still lands here with its status code. Static
/// files and /health are included deliberately (transparent beats flattering).
/// </summary>
public sealed class RequestMetricsMiddleware(
    RequestDelegate next, RequestMetricsAggregator aggregator, TimeProvider timeProvider)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            aggregator.Record(
                timeProvider.GetUtcNow(),
                RouteTemplate(context),
                context.Response.StatusCode,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>The route TEMPLATE, not the concrete URL — "/api/policies/{id}" for every policy, so
    /// per-endpoint cardinality stays at the number of routes no matter the traffic.</summary>
    private static string? RouteTemplate(HttpContext context)
    {
        var endpoint = context.GetEndpoint() as Microsoft.AspNetCore.Routing.RouteEndpoint;
        var pattern = endpoint?.RoutePattern.RawText;
        return string.IsNullOrEmpty(pattern) ? context.Request.Path.Value : pattern;
    }
}
