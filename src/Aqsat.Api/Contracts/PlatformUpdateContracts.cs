namespace Aqsat.Api.Contracts;

public sealed record PlatformStatusDto(string CurrentVersion, bool MaintenanceModeActive, DateTimeOffset? MaintenanceEstimatedEndsAt);

public sealed record UpdatePackageDto(
    Guid Id, string Version, string ReleaseNotesFa, bool HasDbMigration, bool IsSecurityUpdate, DateTimeOffset PublishedAt);

/// <summary>docs/TASKS.md Task 23 — the release pipeline's payload, produced by
/// scripts/release/sign-package.sh and submitted by scripts/release/register-package.sh. The
/// signature is verified server-side before this ever becomes a row (IPackageSignatureVerifier) —
/// an unsigned or tampered submission never reaches the catalog, exactly the same rule
/// Aqsat.Updater enforces again at apply time.</summary>
public sealed record RegisterPackageRequest(
    string Version, string ImageTag, string Sha256, string SignatureBase64, string ReleaseNotesFa,
    string? MinimumFromVersion, bool HasDbMigration, bool IsSecurityUpdate);

public sealed record RequestOtpResponse(bool Sent, string? MobileMasked);

public sealed record StartUpdateRequest(string OtpCode);

public sealed record RollbackRequest(string ToImageTag);

public sealed record UpdateRunDto(
    Guid Id, string FromVersion, string ToVersion, string StartedByFullName, DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt, string Status, string CurrentStage, int ProgressPercent,
    string? ErrorMessage, string? ErrorDetail);

public sealed record UpdateStageLogDto(int StageNo, string StageName, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, bool Succeeded, string? Output);

public sealed record UpdateRunDetailDto(UpdateRunDto Run, IReadOnlyList<UpdateStageLogDto> Stages);
