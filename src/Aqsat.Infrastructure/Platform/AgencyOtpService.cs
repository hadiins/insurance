using System.Security.Cryptography;
using Aqsat.Application.Platform;
using Aqsat.Application.Sms;
using Microsoft.Extensions.Caching.Memory;

namespace Aqsat.Infrastructure.Platform;

/// <summary>
/// Same 6-digit/5-minute/single-use contract as PlatformOtpService, keyed by agency — the OTP
/// goes to the configured agency-manager mobile, not the logged-in user, so the cache key must
/// be the agency too (two users of one agency share one pending code, which is correct: the code
/// authorizes the agency's action, not the person).
/// </summary>
public sealed class AgencyOtpService(ISmsSender smsSender, IMemoryCache cache) : IAgencyOtpService
{
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    public async Task<bool> SendAsync(Guid agencyId, string mobile, CancellationToken ct = default)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        var text = $"کد تأیید عملیات حساس دفتر اقساط: {code}\nاین کد تا ۵ دقیقه معتبر است.";
        var sent = await smsSender.SendAsync(mobile, text, agencyId, ct);
        if (!sent)
        {
            return false;
        }

        cache.Set(CacheKey(agencyId), code, Expiry);
        return true;
    }

    public bool Verify(Guid agencyId, string code)
    {
        var key = CacheKey(agencyId);
        if (!cache.TryGetValue(key, out string? expectedCode) || expectedCode is null || expectedCode != code)
        {
            return false;
        }

        cache.Remove(key);
        return true;
    }

    private static string CacheKey(Guid agencyId) => $"agency-otp:{agencyId}";
}
