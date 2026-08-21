namespace Aqsat.Application.ApiIr;

public readonly record struct ShahkarResult(bool Matched);

/// <summary>
/// docs/PHASE-1-SPEC.md §6 — the six api.ir services Phase 1 uses. Every call is logged for cost
/// (docs/TASKS.md Task 14) and, where the spec says so, cached (CLAUDE.md rule 26). Sandbox by
/// default: SendSms/SmsOTP/ChequeColor/CallOTP are billed, so unless `ApiIr:AllowPaidEndpoints` is
/// explicitly on, calls route to `/api/Sandbox/Echo` instead of the real endpoint.
/// </summary>
public interface IApiIrClient
{
    /// <summary>Cached until midnight — a holiday lookup for the same date never re-fires same-day.
    /// Null means "no real answer available" (sandboxed or the call failed) — the caller
    /// (Aqsat.Infrastructure.ApiIr.ApiIrHolidayChecker) decides the fallback, never this client.</summary>
    Task<bool?> IsHolidayAsync(DateOnly date, Guid agencyId, CancellationToken ct = default);

    /// <summary>Cached forever (national ID + mobile match rarely changes).</summary>
    Task<ShahkarResult?> ShahkarLiteAsync(string nationalId, string mobile, Guid agencyId, CancellationToken ct = default);

    /// <summary>Cached 30 days.</summary>
    Task<string?> ChequeColorAsync(string sayadId, Guid agencyId, CancellationToken ct = default);

    /// <summary>Never cached — every call is a real (or sandboxed) send.</summary>
    Task<bool> SendSmsAsync(string mobile, string text, Guid agencyId, CancellationToken ct = default);

    Task<bool> SmsOtpAsync(string mobile, Guid agencyId, CancellationToken ct = default);

    Task<bool> CallOtpAsync(string mobile, Guid agencyId, CancellationToken ct = default);
}
