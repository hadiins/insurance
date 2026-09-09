namespace Aqsat.Domain.Enums;

/// <summary>What happened on the security dashboard's feed. Grouped/queried by value — keep stable.</summary>
public enum SecurityEventType : byte
{
    FailedLogin = 1,
    SuccessfulLogin = 2,
    RateLimitRejection = 3,
    PermissionDenied = 4,
    SensitiveSettingChanged = 5,
    SuspiciousActivity = 6,
}

public enum SecuritySeverity : byte
{
    Info = 1,
    Warning = 2,
    Critical = 3,
}

/// <summary>The observable a threshold rule evaluates. One enum so rules, evaluation and the UI
/// stay in sync without stringly-typed metric names.</summary>
public enum AlertMetric : byte
{
    ErrorRatePercent = 1,
    LatencyP95Ms = 2,
    CpuPercent = 3,
    ProcessMemoryMb = 4,
    DbProbeMs = 5,
    DiskFreeGb = 6,
    FailedLoginCount = 7,
    HealthDown = 8,
}

public enum AlertComparator : byte
{
    GreaterThan = 1,
    LessThan = 2,
}

public enum AlertStatus : byte
{
    Active = 1,
    Acknowledged = 2,
    Resolved = 3,
}
