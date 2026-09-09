using Aqsat.Application.Common;
using Aqsat.Application.Numbering;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Risk;

public sealed record NetworkRiskResultDto(
    string AgencyName,
    string? InsurerName,
    bool IsOwnAgency,
    int Score,
    RiskLevel RiskLevel,
    RiskDecision Decision,
    int OverdueCount,
    int MaxDaysOverdue,
    int ReturnedChequeCount,
    decimal OnTimeRatePercent,
    int TenureMonths,
    int SettledInstallmentCount,
    DateTimeOffset CalculatedAt);

public sealed record NetworkRiskLookupDto(
    bool IsEnabled,
    string QueryKind,
    IReadOnlyList<NetworkRiskResultDto> Results);

/// <summary>
/// Phase 2B-1 — the cross-agency read half. One lookup by national ID (HMAC match key) or vehicle
/// plate, returning STATUS ONLY (owner decision 2026-09-07): no amounts, no identity — the person
/// is represented solely by the queried key, each result row carries nothing but the source
/// agency's name/insurer and the assessment's status figures. Reads are gated by the platform
/// owner's RiskNetworkSettings switch; while OFF the response says so explicitly rather than
/// returning an empty list that would read as "no history" (rule 17). Every lookup writes one
/// audit row in the same transaction (rule 29) — internal only: the source agency never learns who
/// queried.
/// </summary>
public sealed class NetworkRiskQueryService(AppDbContext dbContext, IFieldEncryptor fieldEncryptor)
{
    public async Task<NetworkRiskLookupDto> LookupAsync(
        string? nationalId, string? plate, Guid currentUserId, string userDisplayName,
        CancellationToken ct = default)
    {
        var callerAgencyId = AgencyContext.Current
            ?? throw new RiskAssessmentException("دامنهٔ نمایندگی نامعتبر است.");

        var settings = await dbContext.RiskNetworkSettings.AsNoTracking()
            .SingleOrDefaultAsync(ct);

        var queryKind = "nationalId";
        var queryLabel = string.Empty;
        List<byte[]> hashes;

        if (!string.IsNullOrWhiteSpace(nationalId))
        {
            var normalized = DigitNormalizer.ToLatin(nationalId).Trim();
            if (!NationalIdValidator.IsValid(normalized))
            {
                throw new RiskAssessmentException("کد ملی نامعتبر است.");
            }

            queryLabel = normalized;
            hashes = [fieldEncryptor.Hash(normalized)];
        }
        else if (!string.IsNullOrWhiteSpace(plate))
        {
            var parsed = PlateParser.Parse(plate);
            if (!parsed.IsParsed || parsed.Normalized is null)
            {
                throw new RiskAssessmentException("قالب پلاک شناخته نشد.");
            }

            queryKind = "plate";
            queryLabel = parsed.Normalized;
            hashes = await dbContext.NetworkRiskPlateIndex.AsNoTracking()
                .Where(i => i.PlateNormalized == parsed.Normalized)
                .Select(i => i.NationalIdHash)
                .ToListAsync(ct);
        }
        else
        {
            throw new RiskAssessmentException("کد ملی یا شماره پلاک را وارد کنید.");
        }

        // The switch is checked AFTER the input is validated but BEFORE any data is read: a
        // disabled network must answer IsEnabled=false (an explicit state, not a silent empty
        // list) and must not leak anything, including whether the key exists at all.
        if (settings?.IsEnabled != true)
        {
            return new NetworkRiskLookupDto(false, queryKind, []);
        }

        var profiles = new List<NetworkRiskProfile>();
        foreach (var hash in hashes)
        {
            profiles.AddRange(await dbContext.NetworkRiskProfiles.AsNoTracking()
                .Where(p => p.NationalIdHash == hash)
                .ToListAsync(ct));
        }

        var agencyIds = profiles.Select(p => p.AgencyId).Distinct().ToList();
        var orgNames = await dbContext.Organizations.AsNoTracking()
            .Where(o => agencyIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => new { o.Name, o.InsurerName }, ct);

        var results = profiles
            .Where(p => orgNames.ContainsKey(p.AgencyId))
            .OrderByDescending(p => p.CalculatedAt)
            .Select(p => new NetworkRiskResultDto(
                orgNames[p.AgencyId].Name,
                orgNames[p.AgencyId].InsurerName,
                p.AgencyId == callerAgencyId,
                p.Score,
                p.RiskLevel,
                p.Decision,
                p.OverdueCount,
                p.MaxDaysOverdue,
                p.ReturnedChequeCount,
                p.OnTimeRatePercent,
                p.TenureMonths,
                p.SettledInstallmentCount,
                p.CalculatedAt))
            .ToList();

        var kindFa = queryKind == "plate" ? "پلاک" : "کد ملی";
        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = callerAgencyId,
            UserId = currentUserId,
            UserDisplayName = userDisplayName,
            EntityType = "NetworkRiskLookup",
            EntityId = Guid.Empty,
            PolicyId = Guid.Empty,
            Action = AuditAction.NetworkRiskLookup,
            Description =
                $"استعلام شبکه‌ای ریسک با {kindFa} «{queryLabel}» انجام شد — {results.Count} نتیجه از {agencyIds.Count} نمایندگی",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync(ct);

        return new NetworkRiskLookupDto(true, queryKind, results);
    }
}
