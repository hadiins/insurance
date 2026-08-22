namespace Aqsat.Api.Contracts;

public sealed record PlatformStatusDto(string CurrentVersion, bool MaintenanceModeActive, DateTimeOffset? MaintenanceEstimatedEndsAt);

public sealed record UpdatePackageDto(
    Guid Id, string Version, string ReleaseNotesFa, bool HasDbMigration, bool IsSecurityUpdate, DateTimeOffset PublishedAt);

public sealed record RequestOtpResponse(bool Sent, string? MobileMasked);

public sealed record StartUpdateRequest(string OtpCode);

public sealed record RollbackRequest(string ToImageTag);

public sealed record UpdateRunDto(
    Guid Id, string FromVersion, string ToVersion, string StartedByFullName, DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt, string Status, string CurrentStage, int ProgressPercent,
    string? ErrorMessage, string? ErrorDetail);

public sealed record UpdateStageLogDto(int StageNo, string StageName, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, bool Succeeded, string? Output);

public sealed record UpdateRunDetailDto(UpdateRunDto Run, IReadOnlyList<UpdateStageLogDto> Stages);
