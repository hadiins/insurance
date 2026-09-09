using System.Security.Cryptography;
using Aqsat.Application.Platform;
using Aqsat.Application.Sms;
using Microsoft.Extensions.Caching.Memory;

namespace Aqsat.Infrastructure.Platform;

/// <summary>
/// Same 6-digit/5-minute/single-use contract as PlatformOtpService, keyed by the mobile being
/// verified. Guid.Empty as the agencyId makes ApiIrClient fall back to the platform SMS key —
/// there is no agency (and no OrgSettings row) to read an agency-specific key from.
/// </summary>
public sealed class SignupOtpService(ISmsSender smsSender, IMemoryCache cache) : ISignupOtpService
{
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    public async Task<bool> SendAsync(string mobile, CancellationToken ct = default)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        var text = $"کد تأیید ثبت‌نام Credix: {code}\nاین کد تا ۵ دقیقه معتبر است.";
        var sent = await smsSender.SendAsync(mobile, text, Guid.Empty, ct);
        if (!sent)
        {
            return false;
        }

        cache.Set(CacheKey(mobile), code, Expiry);
        return true;
    }

    public bool Verify(string mobile, string code)
    {
        var key = CacheKey(mobile);
        if (!cache.TryGetValue(key, out string? expectedCode) || expectedCode is null || expectedCode != code)
        {
            return false;
        }

        cache.Remove(key);
        return true;
    }

    private static string CacheKey(string mobile) => $"signup-otp:{mobile}";
}
