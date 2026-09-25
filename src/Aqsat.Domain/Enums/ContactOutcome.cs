namespace Aqsat.Domain.Enums;

/// <summary>What came out of a collection contact. An enum, never free text — CLAUDE.md rule 8
/// forbids a free-text field about a person.</summary>
public enum ContactOutcome : byte
{
    NoAnswer = 1,
    Promised = 2,
    Paid = 3,
    Refused = 4,
    WrongNumber = 5,
}
