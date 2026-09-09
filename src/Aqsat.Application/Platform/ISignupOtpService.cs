namespace Aqsat.Application.Platform;

/// <summary>
/// Pre-registration mobile verification — keyed by the mobile being verified, not a user or
/// agency (neither exists yet at signup time). The SMS goes out on the platform's own api.ir key
/// because there is no agency settings row to read one from.
/// </summary>
public interface ISignupOtpService
{
    /// <summary>False means the SMS never went out — no code is cached, so a retry generates a
    /// fresh one and Verify can never accept a code nobody received.</summary>
    Task<bool> SendAsync(string mobile, CancellationToken ct = default);

    /// <summary>Single-use: a correct code is consumed on first successful verify.</summary>
    bool Verify(string mobile, string code);
}
