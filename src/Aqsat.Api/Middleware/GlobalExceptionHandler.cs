using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Middleware;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is DbUpdateConcurrencyException concurrencyException)
        {
            await WriteConcurrencyConflictAsync(httpContext, concurrencyException, cancellationToken);
            return true;
        }

        logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "خطای غیرمنتظره در سرور رخ داده است.",
            Type = "https://httpstatuses.com/500",
            Instance = httpContext.Request.Path,
        };

        if (environment.IsDevelopment())
        {
            problemDetails.Detail = exception.ToString();
        }

        httpContext.Response.StatusCode = problemDetails.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    /// <summary>
    /// docs/CONCURRENCY.md §2 — centralized here so every write endpoint gets rowversion conflict
    /// handling "for free" (checklist item: "DbUpdateConcurrencyException handled everywhere, with
    /// diffs shown") instead of every controller repeating the same catch block. Never discards the
    /// caller's attempted values — both sides of every changed field are returned so the UI can
    /// offer "keep mine" / "accept theirs".
    /// </summary>
    private async Task WriteConcurrencyConflictAsync(
        HttpContext httpContext, DbUpdateConcurrencyException exception, CancellationToken cancellationToken)
    {
        logger.LogWarning(exception, "Concurrency conflict on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        var entry = exception.Entries.Single();
        var current = await entry.GetDatabaseValuesAsync(cancellationToken);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Type = "https://httpstatuses.com/409",
            Instance = httpContext.Request.Path,
        };

        if (current is null)
        {
            problemDetails.Title = "این رکورد توسط کاربر دیگری حذف شده است.";
            problemDetails.Extensions["reason"] = "deleted";
        }
        else
        {
            problemDetails.Title = "این رکورد هم‌زمان توسط کاربر دیگری تغییر کرده است.";
            problemDetails.Extensions["reason"] = "modified";
            problemDetails.Extensions["fields"] = entry.Properties
                .Where(p => !Equals(p.CurrentValue, current[p.Metadata.Name]))
                .Select(p => new { field = p.Metadata.Name, yours = p.CurrentValue, theirs = current[p.Metadata.Name] })
                .ToList();
        }

        httpContext.Response.StatusCode = problemDetails.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
    }
}
