namespace Aqsat.Domain.Enums;

/// <summary>How a collection contact with the customer happened.</summary>
public enum ContactChannel : byte
{
    Call = 1,
    Sms = 2,
    InPerson = 3,
    Portal = 4,
}
