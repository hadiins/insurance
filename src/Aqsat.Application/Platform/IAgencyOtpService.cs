namespace Aqsat.Application.Platform;

/// <summary>
/// Danger-zone two-factor: before an agency's data can be wiped, a 6-digit code is SMS'd to the
/// agency manager's mobile configured in OrgSettings (owner decision 2026-09-02 — never to
/// whoever happens to be logged in). Same shape as IPlatformOtpService, keyed by agency instead
/// of user because the recipient is a setting, not the caller.
/// </summary>
public interface IAgencyOtpService
{
    /// <summary>False means the SMS never went out — no code is cached, so a retry generates a
    /// fresh one and Verify can never accept a code nobody received.</summary>
    Task<bool> SendAsync(Guid agencyId, string mobile, CancellationToken ct = default);

    /// <summary>Single-use: a correct code is consumed on first successful verify.</summary>
    bool Verify(Guid agencyId, string code);
}
