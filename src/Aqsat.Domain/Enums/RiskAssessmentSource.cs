namespace Aqsat.Domain.Enums;

/// <summary>Who triggered the assessment — the manual button or the nightly Hangfire job
/// (owner decision 2026-09-03).</summary>
public enum RiskAssessmentSource : byte
{
    Manual = 1,
    Scheduled = 2,
}
