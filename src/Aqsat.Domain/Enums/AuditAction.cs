namespace Aqsat.Domain.Enums;

public enum AuditAction : byte
{
    Created = 1,
    Updated = 2,
    Deleted = 3,
    PaymentRecorded = 4,
    LockForceReleased = 5,
    PaymentReversed = 6,
}
