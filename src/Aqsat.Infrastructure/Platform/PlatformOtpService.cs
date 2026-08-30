using System.Security.Cryptography;
using Aqsat.Application.Platform;
using Aqsat.Application.Sms;
using Microsoft.Extensions.Caching.Memory;

namespace Aqsat.Infrastructure.Platform;

/// <summary>
/// 6-digit code, 5-minute expiry, keyed by userId in the process's own memory cache — deliberately
/// not a database table: this is exactly as ephemeral as a login session, and adding a persisted
/// table for something that must never survive a restart (nor should it) would be the wrong shape.
/// </summary>
public sealed class PlatformOtpService(ISmsSender smsSender, IMemoryCache cache) : IPlatformOtpService
{
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    public async Task<bool> SendAsync(Guid userId, string mobile, Guid agencyId, CancellationToken ct = default)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        var text = $"کد تأیید به‌روزرسانی سامانه: {code}\nاین کد تا ۵ دقیقه معتبر است.";
        var sent = await smsSender.SendAsync(mobile, text, agencyId, ct);
        if (!sent)
        {
            // No code is cached when nothing was actually sent — otherwise Verify would accept a
            // code the owner never received, and a "retry" would keep verifying the phantom.
            return false;
        }

        cache.Set(CacheKey(userId), code, Expiry);
        return true;
    }

    public bool Verify(Guid userId, string code)
    {
        var key = CacheKey(userId);
        if (!cache.TryGetValue(key, out string? expectedCode) || expectedCode is null)
        {
            return false;
        }

        // Ordinary equality is fine here (unlike Aqsat.Updater's shared-token check): the attacker
        // surface is one authenticated Platform.Owner session guessing a 6-digit code within a
        // 5-minute window, not an unauthenticated network endpoint — rate limiting (CLAUDE.md
        // Task 19) already covers the brute-force case.
        if (expectedCode != code)
        {
            return false;
        }

        cache.Remove(key);
        return true;
    }

    private static string CacheKey(Guid userId) => $"platform-otp:{userId}";
}
