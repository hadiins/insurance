namespace Aqsat.Application.ApiIr;

/// <summary>استعلام تعداد چک برگشتی (/api/sw1/UnpaidCheque). Amounts are RIALS as api.ir returns
/// them — callers convert to toman (rule 19). Null fields mean "no real answer" (sandboxed call or
/// failure); a null return itself means the call was rejected outright.</summary>
public sealed record UnpaidChequeResult(int? Count, decimal? SumAmountRial, decimal? SumBouncedAmountRial);

/// <summary>استعلام تسهیلات فعال بانکی (/api/sw1/ActiveLoans). Amounts are RIALS; every amount is
/// nullable because api.ir returns `info` as null when the person has no active facilities.</summary>
public sealed record ActiveLoansResult(
    int? Count,
    decimal? TotalAmountRial,
    decimal? DebtTotalAmountRial,
    decimal? PastExpiredTotalAmountRial,
    decimal? DeferredTotalAmountRial,
    decimal? SuspiciousTotalAmountRial,
    decimal? DishonoredRial);

/// <summary>
/// docs/PHASE-1-SPEC.md §6 — the api.ir services this product actually calls. Every call is logged
/// for cost (docs/TASKS.md Task 14) and, where the spec says so, cached (CLAUDE.md rule 26). Sandbox
/// by default: SendSms/SmsOTP/ChequeColor/UnpaidCheque/ActiveLoans are billed, so unless
/// `ApiIr:AllowPaidEndpoints` is explicitly on, calls route to `/api/Sandbox/Echo` instead of the
/// real endpoint.
///
/// Owner decision 2026-09-23 (see docs/PHASE-1-SPEC.md §6) dropped the two services that had no
/// caller anywhere in the product — ShahkarLite (national-id/mobile match, 550) and CallOTP (voice
/// one-time code, 95) — together with their endpoint methods, cost constants and tests, so the
/// account is never billed for a capability nothing uses. IsHolidayAsync is kept but deliberately
/// wired to nobody: the registered IHolidayChecker is WeekendOnlyHolidayChecker (Friday-only), so
/// IsHolidayAsync stays implemented, tested and sandbox-routable, so switching the paid lookup back
/// on is the thin adapter (Aqsat.Infrastructure.ApiIr) plus one registration line — no schema
/// change, no data migration.
/// </summary>
public interface IApiIrClient
{
    /// <summary>PARKED — no caller by owner decision (see the interface summary); the registered
    /// IHolidayChecker falls back to the Friday-only weekend checker instead. Kept, tested and
    /// sandbox-routable so re-enabling is a one-line DI change. Cached until midnight — a holiday
    /// lookup for the same date never re-fires same-day. Null means "no real answer available"
    /// (sandboxed or the call failed) — the caller decides the fallback, never this client.</summary>
    Task<bool?> IsHolidayAsync(DateOnly date, Guid agencyId, CancellationToken ct = default);

    /// <summary>Cached 30 days.</summary>
    Task<string?> ChequeColorAsync(string sayadId, Guid agencyId, CancellationToken ct = default);

    /// <summary>Never cached — every call is a real (or sandboxed) send.</summary>
    Task<bool> SendSmsAsync(string mobile, string text, Guid agencyId, CancellationToken ct = default);

    Task<bool> SmsOtpAsync(string mobile, string code, Guid agencyId, CancellationToken ct = default);

    /// <summary>Never cached — the issuance-time credit check must see the customer's situation as
    /// of right now. Null = the call was rejected (bad key, no credit); a non-null result with null
    /// fields = sandboxed, no real data.</summary>
    Task<UnpaidChequeResult?> UnpaidChequeAsync(string nationalCode, Guid agencyId, CancellationToken ct = default);

    /// <summary>Never cached, same contract as UnpaidChequeAsync.</summary>
    Task<ActiveLoansResult?> ActiveLoansAsync(string nationalCode, Guid agencyId, CancellationToken ct = default);
}
