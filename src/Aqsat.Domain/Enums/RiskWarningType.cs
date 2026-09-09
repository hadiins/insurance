namespace Aqsat.Domain.Enums;

/// <summary>Early-warning triggers (docs Phase 2A §19) — the message itself is composed at write
/// time (CLAUDE.md rule 31), so this enum only drives filtering and icons.</summary>
public enum RiskWarningType : byte
{
    LevelEscalation = 1,
    ScoreDrop = 2,
    NearCreditLimit = 3,
    RapidDebtGrowth = 4,
    NewBouncedCheque = 5,
}
