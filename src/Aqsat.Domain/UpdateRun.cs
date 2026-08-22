using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// docs/UPDATE-SYSTEM.md §4/§10 — one row per attempted update, the durable record Task 21's
/// Aqsat.Updater does NOT keep (its own progress tracking is in-memory only, by design: it's a
/// short-lived sidecar, this database is the system of record). The panel persists this by polling
/// the Updater's GET /progress/{id} and relaying both to SignalR and to this table — so closing the
/// browser mid-update (docs/UPDATE-SYSTEM.md §5's explicit scenario) never loses anything: on
/// reconnect the panel just re-reads the latest row.
/// </summary>
public class UpdateRun : SoftDeletableEntity
{
    public Guid PackageId { get; set; }
    public UpdatePackage Package { get; set; } = default!;

    public string FromVersion { get; set; } = default!;
    public string ToVersion { get; set; } = default!;

    public Guid StartedByUserId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public UpdateRunStatus Status { get; set; } = UpdateRunStatus.Running;
    public string CurrentStage { get; set; } = default!;
    public int ProgressPercent { get; set; }

    public string? BackupPath { get; set; }
    public long? BackupSizeBytes { get; set; }

    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorDetail { get; set; }

    /// <summary>The Updater-side run id (its in-memory UpdateProgress.RunId) — the join key the
    /// panel polls GET /progress/{id} with. Distinct from this row's own Id on purpose: this Id is
    /// what the panel's UI and URLs use, the Updater's is an implementation detail of that service.</summary>
    public Guid UpdaterRunId { get; set; }
}
