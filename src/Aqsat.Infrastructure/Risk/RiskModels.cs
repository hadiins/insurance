using Aqsat.Domain.Enums;

namespace Aqsat.Infrastructure.Risk;

public enum RiskFactorSeverity : byte
{
    Positive = 1,
    Info = 2,
    Medium = 3,
    High = 4,
}

/// <summary>One explainability item (docs Phase 2A §10/§26) — structured, composed at write time;
/// Impact is the signed point contribution relative to the neutral 700 baseline.</summary>
public sealed record RiskFactor(string Code, string TitleFa, string DetailFa, int Impact, RiskFactorSeverity Severity);

/// <summary>A rule that fired during assessment (docs Phase 2A §11/§12) — fixed built-in rules with
/// per-agency thresholds (owner decision 2026-09-03); ForcedLevel/ForcedDecision carry the override
/// the doc's decision engine requires.</summary>
public sealed record TriggeredRule(
    string Code, string NameFa, string DescriptionFa, RiskLevel? ForcedLevel, RiskDecision? ForcedDecision, bool IsPositive);
