namespace Aqsat.Domain.Enums;

public enum UpdateRunStatus : byte
{
    Running = 1,
    Success = 2,
    Failed = 3,
    RolledBack = 4,
}
