namespace Aqsat.Domain.Enums;

/// <summary>
/// How the risk DECISION interacts with installment issuance (owner decision 2026-09-03: all three
/// modes exist, selected per agency). Informational = display only; SoftBlock = DECLINE shows a red
/// warning the agent may pass with a recorded reason; HardBlock = DECLINE refuses issuance.
/// </summary>
public enum IssuanceGateMode : byte
{
    Informational = 1,
    SoftBlock = 2,
    HardBlock = 3,
}
