using System.Text.Json;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Risk;

public sealed class RiskAssessmentException(string message) : Exception(message);

/// <summary>Everything one assessment produced — the persisted snapshot plus the structured
/// factors/rules for the DTO layer (InsufficientData=true means no evidence to score yet).</summary>
public sealed record RiskAssessmentResult(
    RiskAssessment Assessment,
    RiskFeatures Features,
    IReadOnlyList<RiskFactor> Factors,
    IReadOnlyList<TriggeredRule> Rules,
    bool InsufficientData);

/// <summary>
/// Orchestrates one assessment (docs Phase 2A §2): features → weighted score → rules → decision →
/// credit limit → snapshot + warnings + audit, all persisted in ONE SaveChanges so the assessment
/// and its audit row commit together (CLAUDE.md rule 29). Operator calls run in the caller's RLS
/// scope; the nightly Hangfire job (stage 4) calls the same path with the system actor.
/// </summary>
public sealed class RiskAssessmentService(
    AppDbContext dbContext,
    RiskFeatureCalculator featureCalculator,
    IFieldEncryptor fieldEncryptor,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RiskAssessmentResult> AssessAsync(
        Guid customerId, RiskAssessmentSource source, Guid currentUserId, string userDisplayName,
        CancellationToken ct = default)
    {
        var agencyId = AgencyContext.Current
            ?? throw new RiskAssessmentException("دامنهٔ نمایندگی نامعتبر است.");

        var customer = await dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new RiskAssessmentException("مشتری یافت نشد.");

        var settings = await GetSettingsAsync(agencyId, ct);
        var writeOffDays = await dbContext.OrgSettings.AsNoTracking()
            .Where(o => o.OrganizationId == agencyId)
            .Select(o => o.DefaultWriteOffDays)
            .FirstOrDefaultAsync(ct);
        if (writeOffDays == 0)
        {
            writeOffDays = 30;
        }

        var features = await featureCalculator.CalculateAsync(customerId, writeOffDays, ct);
        if (!features.HasAnyEvidence)
        {
            return new RiskAssessmentResult(null!, features, Array.Empty<RiskFactor>(),
                Array.Empty<TriggeredRule>(), InsufficientData: true);
        }

        var previous = await dbContext.RiskAssessments.AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.CalculatedAt)
            .FirstOrDefaultAsync(ct);

        var manualLimit = await dbContext.CustomerCreditLimits.AsNoTracking()
            .Where(l => l.CustomerId == customerId)
            .OrderByDescending(l => l.SetAt)
            .FirstOrDefaultAsync(ct);

        var now = timeProvider.GetUtcNow();
        var (score, factors) = CreditScoreEngine.Calculate(features, settings);
        var bandLevel = CreditScoreEngine.LevelForScore(score, settings);

        // Pass 1 without the limit (rule 5 needs the limit, which needs the final level first —
        // the level-forcing rules never depend on the limit, so ordering is safe).
        var pass1Rules = RiskRuleEngine.Evaluate(features, settings, effectiveCreditLimit: 0);
        var forcedLevel = RiskRuleEngine.ForcedLevel(pass1Rules);
        var finalLevel = forcedLevel is { } forced && forced > bandLevel ? forced : bandLevel;

        var (creditLimit, isOverride) = CreditLimitEngine.Compute(finalLevel, settings, manualLimit?.LimitToman);
        var rules = RiskRuleEngine.Evaluate(features, settings, creditLimit);
        var decision = RiskDecisionEngine.Decide(score, finalLevel, rules, settings);

        var assessment = new RiskAssessment
        {
            // Client-side sequential GUID (rule 1) — the warnings' FK and the audit row's EntityId
            // must reference it inside the same SaveChanges, before SQL Server ever sees it.
            Id = SequentialGuidGenerator.Next(),
            AgencyId = agencyId,
            CustomerId = customerId,
            Score = score,
            RiskLevel = finalLevel,
            Decision = decision,
            CurrentDebtToman = features.CurrentDebtToman,
            OverdueAmountToman = features.OverdueAmountToman,
            OverdueCount = features.OverdueCount,
            MaxDaysOverdue = features.MaxDaysOverdue,
            ReturnedChequeCount = features.ReturnedChequeCount,
            OnTimeRatePercent = features.OnTimeRatePercent ?? 0,
            SettledInstallmentCount = features.SettledInstallmentCount,
            TenureMonths = features.TenureMonths,
            CreditExposureToman = features.CurrentDebtToman,
            CreditLimitToman = creditLimit,
            CreditLimitIsOverride = isOverride,
            FactorsJson = JsonSerializer.Serialize(factors, JsonOptions),
            TriggeredRulesJson = JsonSerializer.Serialize(rules, JsonOptions),
            Source = source,
            CalculatedAt = now,
        };
        dbContext.RiskAssessments.Add(assessment);

        foreach (var warning in BuildWarnings(customer.FullName, previous, assessment, features, settings))
        {
            dbContext.RiskWarnings.Add(new RiskWarning
            {
                Id = SequentialGuidGenerator.Next(),
                AgencyId = agencyId,
                CustomerId = customerId,
                AssessmentId = assessment.Id,
                Type = warning.Type,
                Message = warning.Message,
                CreatedAt = now,
            });
        }

        // Doc §15 — a MANUAL_REVIEW decision opens a case in the review queue, one open case per
        // customer: re-assessing an already-queued customer refreshes the assessment, not the queue.
        if (decision == RiskDecision.ManualReview)
        {
            var hasOpenReview = await dbContext.ManualReviews.AsNoTracking()
                .AnyAsync(r => r.CustomerId == customerId
                    && (r.Status == ManualReviewStatus.Pending
                        || r.Status == ManualReviewStatus.InReview
                        || r.Status == ManualReviewStatus.RequestMoreInfo), ct);
            if (!hasOpenReview)
            {
                dbContext.ManualReviews.Add(new ManualReview
                {
                    Id = SequentialGuidGenerator.Next(),
                    AgencyId = agencyId,
                    CustomerId = customerId,
                    AssessmentId = assessment.Id,
                    Status = ManualReviewStatus.Pending,
                    RecommendedDecision = decision,
                    CreatedAt = now,
                });
            }
        }

        // Phase 2B-1 cross-agency sharing — the status-only derived row, upserted in this same
        // SaveChanges (rule 29). Written even while the platform switch is OFF: it is derived
        // data with no amounts or identity, so enabling the switch later needs no backfill.
        // A customer with no usable national ID simply never gets a network row.
        var nationalIdHash = customer.NationalIdHash;
        if (nationalIdHash is null && !string.IsNullOrWhiteSpace(customer.NationalId))
        {
            var normalized = DigitNormalizer.ToLatin(customer.NationalId).Trim();
            if (normalized.Length > 0)
            {
                nationalIdHash = fieldEncryptor.Hash(normalized);
            }
        }

        if (nationalIdHash is not null)
        {
            // The customer row was loaded no-tracking, so the freshly derived hash is persisted
            // with a targeted update — later paths (dedupe, the plate-index sync) then read it
            // instead of recomputing. Derived data: safe even if the assessment below rolls back.
            if (customer.NationalIdHash is null)
            {
                await dbContext.Customers
                    .Where(c => c.Id == customerId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.NationalIdHash, nationalIdHash), ct);
            }

            var profile = await dbContext.NetworkRiskProfiles
                .FirstOrDefaultAsync(p => p.AgencyId == agencyId && p.NationalIdHash == nationalIdHash, ct);
            if (profile is null)
            {
                dbContext.NetworkRiskProfiles.Add(new NetworkRiskProfile
                {
                    Id = SequentialGuidGenerator.Next(),
                    AgencyId = agencyId,
                    CustomerId = customerId,
                    NationalIdHash = nationalIdHash,
                    Score = score,
                    RiskLevel = finalLevel,
                    Decision = decision,
                    OverdueCount = features.OverdueCount,
                    MaxDaysOverdue = features.MaxDaysOverdue,
                    ReturnedChequeCount = features.ReturnedChequeCount,
                    OnTimeRatePercent = features.OnTimeRatePercent ?? 0,
                    TenureMonths = features.TenureMonths,
                    SettledInstallmentCount = features.SettledInstallmentCount,
                    CalculatedAt = now,
                });
            }
            else
            {
                profile.CustomerId = customerId;
                profile.Score = score;
                profile.RiskLevel = finalLevel;
                profile.Decision = decision;
                profile.OverdueCount = features.OverdueCount;
                profile.MaxDaysOverdue = features.MaxDaysOverdue;
                profile.ReturnedChequeCount = features.ReturnedChequeCount;
                profile.OnTimeRatePercent = features.OnTimeRatePercent ?? 0;
                profile.TenureMonths = features.TenureMonths;
                profile.SettledInstallmentCount = features.SettledInstallmentCount;
                profile.CalculatedAt = now;
            }
        }

        var levelFa = LevelFa(finalLevel);
        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = agencyId,
            UserId = currentUserId,
            UserDisplayName = userDisplayName,
            EntityType = nameof(RiskAssessment),
            EntityId = assessment.Id,
            PolicyId = Guid.Empty,
            Action = AuditAction.RiskAssessed,
            Description =
                $"ارزیابی اعتباری مشتری {customer.FullName} انجام شد — امتیاز {score}، سطح {levelFa}، سقف اعتبار {creditLimit:N0} تومان",
            OccurredAt = now,
        });

        await dbContext.SaveChangesAsync(ct);
        return new RiskAssessmentResult(assessment, features, factors, rules, InsufficientData: false);
    }

    public Task<RiskAssessment?> GetLatestAsync(Guid customerId, CancellationToken ct = default) =>
        dbContext.RiskAssessments.AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.CalculatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<List<RiskAssessment>> GetHistoryAsync(Guid customerId, CancellationToken ct = default) =>
        dbContext.RiskAssessments.AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.CalculatedAt)
            .Take(100)
            .ToListAsync(ct);

    /// <summary>The doc's credit-limit view: what the engine recommends, the agency's override,
    /// and which of the two is in force. The recommendation follows the latest assessment's level
    /// (null before the first assessment).</summary>
    public async Task<(decimal? Recommended, decimal? Override, decimal Effective)> GetCreditLimitAsync(
        Guid customerId, CancellationToken ct = default)
    {
        var agencyId = AgencyContext.Current
            ?? throw new RiskAssessmentException("دامنهٔ نمایندگی نامعتبر است.");

        var latest = await GetLatestAsync(customerId, ct);
        var manual = await dbContext.CustomerCreditLimits.AsNoTracking()
            .Where(l => l.CustomerId == customerId)
            .OrderByDescending(l => l.SetAt)
            .FirstOrDefaultAsync(ct);

        decimal? recommended = null;
        if (latest is not null)
        {
            var settings = await GetSettingsAsync(agencyId, ct);
            recommended = CreditLimitEngine.Compute(latest.RiskLevel, settings, manualOverride: null).Limit;
        }

        var effective = manual?.LimitToman ?? recommended ?? 0m;
        return (recommended, manual?.LimitToman, effective);
    }

    public async Task SetCreditLimitAsync(
        Guid customerId, decimal limitToman, string? reason, Guid currentUserId, string userDisplayName,
        CancellationToken ct = default)
    {
        var agencyId = AgencyContext.Current
            ?? throw new RiskAssessmentException("دامنهٔ نمایندگی نامعتبر است.");

        if (limitToman < 0)
        {
            throw new RiskAssessmentException("سقف اعتبار نمی‌تواند منفی باشد.");
        }

        var customer = await dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new RiskAssessmentException("مشتری یافت نشد.");

        var existing = await dbContext.CustomerCreditLimits
            .FirstOrDefaultAsync(l => l.CustomerId == customerId, ct);

        var now = DateTimeOffset.UtcNow;
        Guid overrideId;
        if (existing is null)
        {
            var created = new CustomerCreditLimit
            {
                Id = SequentialGuidGenerator.Next(),
                AgencyId = agencyId,
                CustomerId = customerId,
                LimitToman = limitToman,
                Reason = reason,
                SetByUserId = currentUserId,
                SetAt = now,
            };
            overrideId = created.Id;
            dbContext.CustomerCreditLimits.Add(created);
        }
        else
        {
            existing.LimitToman = limitToman;
            existing.Reason = reason;
            existing.SetByUserId = currentUserId;
            existing.SetAt = now;
            overrideId = existing.Id;
        }

        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = agencyId,
            UserId = currentUserId,
            UserDisplayName = userDisplayName,
            EntityType = nameof(CustomerCreditLimit),
            EntityId = overrideId,
            PolicyId = Guid.Empty,
            Action = AuditAction.RiskAssessed,
            Description = $"سقف اعتبار مشتری {customer.FullName} به {limitToman:N0} تومان تنظیم شد",
            OccurredAt = now,
        });

        await dbContext.SaveChangesAsync(ct);
    }

    /// <summary>The agency's risk configuration — never null: agencies without a row run on the
    /// class defaults until they edit anything.</summary>
    public async Task<RiskSettings> GetSettingsAsync(Guid organizationId, CancellationToken ct = default) =>
        await dbContext.RiskSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct)
        ?? DefaultSettings();

    public static RiskSettings DefaultSettings() => new();

    /// <summary>Doc §19 — each warning's message is composed here, at write time (rule 31).</summary>
    private static IEnumerable<(RiskWarningType Type, string Message)> BuildWarnings(
        string customerName, RiskAssessment? previous, RiskAssessment current, RiskFeatures features, RiskSettings s)
    {
        if (previous is null)
        {
            yield break;
        }

        if (current.RiskLevel > previous.RiskLevel)
        {
            yield return (RiskWarningType.LevelEscalation,
                $"افزایش سطح ریسک مشتری {customerName}: {LevelFa(previous.RiskLevel)} ← {LevelFa(current.RiskLevel)} (امتیاز {current.Score})");
        }

        if (previous.Score - current.Score >= s.ScoreDropWarningPoints)
        {
            yield return (RiskWarningType.ScoreDrop,
                $"افت امتیاز اعتباری مشتری {customerName}: {previous.Score} ← {current.Score}");
        }

        if (current.CreditLimitToman > 0
            && current.CreditExposureToman * 100m / current.CreditLimitToman >= s.CreditLimitUtilizationWarningPercent)
        {
            yield return (RiskWarningType.NearCreditLimit,
                $"بدهی مشتری {customerName} به سقف اعتبار نزدیک است ({current.CreditExposureToman:N0} از {current.CreditLimitToman:N0} تومان)");
        }

        if (previous.CurrentDebtToman > 0
            && (current.CurrentDebtToman - previous.CurrentDebtToman) * 100m / previous.CurrentDebtToman
                >= s.DebtGrowthWarningPercent)
        {
            yield return (RiskWarningType.RapidDebtGrowth,
                $"رشد سریع بدهی مشتری {customerName}: از {previous.CurrentDebtToman:N0} به {current.CurrentDebtToman:N0} تومان");
        }

        if (current.ReturnedChequeCount > previous.ReturnedChequeCount)
        {
            yield return (RiskWarningType.NewBouncedCheque,
                $"چک برگشتی جدید برای مشتری {customerName} ثبت شد ({current.ReturnedChequeCount} چک برگشتی)");
        }
    }

    public static string LevelFa(RiskLevel level) => level switch
    {
        RiskLevel.VeryLow => "ریسک خیلی پایین",
        RiskLevel.Low => "ریسک پایین",
        RiskLevel.Medium => "ریسک متوسط",
        RiskLevel.High => "ریسک بالا",
        _ => "ریسک بحرانی",
    };
}
