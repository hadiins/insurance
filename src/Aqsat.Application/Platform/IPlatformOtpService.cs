namespace Aqsat.Application.Platform;

/// <summary>
/// docs/UPDATE-SYSTEM.md rule 2 — two-factor is mandatory before an update can start, a password
/// alone is not enough. IApiIrClient's SmsOtpAsync/CallOtpAsync are one-way "we sent something"
/// calls with no verification counterpart, so this service owns generating and checking the code
/// itself rather than trusting api.ir to do it — plain SendSmsAsync carries our own generated code.
/// </summary>
public interface IPlatformOtpService
{
    /// <summary>False means the SMS never went out (api.ir rejection, outage, no credit…) — the
    /// caller must surface that instead of promising a code that will never arrive. A failed send
    /// also leaves no cached code behind, so a retry generates a fresh one.</summary>
    Task<bool> SendAsync(Guid userId, string mobile, Guid agencyId, CancellationToken ct = default);

    /// <summary>Single-use: a correct code is consumed on first successful verify, so a leaked or
    /// re-submitted code can never authorize a second update.</summary>
    bool Verify(Guid userId, string code);
}
