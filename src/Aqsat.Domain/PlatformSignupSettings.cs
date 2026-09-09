using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// Singleton row (at most one) holding the platform-wide switch for the public self-serve agency
/// signup form (/signup). Replaces the Platform:AllowAgencySignup config key (owner request
/// 2026-09-08): the owner flips it from «مدیریت نمایندگی‌ها» and it takes effect instantly — no
/// appsettings edit, no redeploy. Platform-level exactly like RiskNetworkSettings: no AgencyId,
/// not RLS-scoped, edited only through a Platform.Owner-gated endpoint. Absent row = closed,
/// the safe default for a fresh install.
/// </summary>
public class PlatformSignupSettings : SoftDeletableEntity
{
    public bool AllowAgencySignup { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedByUserId { get; set; }
}
