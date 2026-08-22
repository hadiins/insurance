using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>docs/UPDATE-SYSTEM.md §4 — per-stage history under one UpdateRun, immutable once
/// written (CLAUDE.md's audit spirit: append, never rewrite).</summary>
public class UpdateStageLog : SoftDeletableEntity
{
    public Guid RunId { get; set; }
    public UpdateRun Run { get; set; } = default!;

    public int StageNo { get; set; }
    public string StageName { get; set; } = default!;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public bool Succeeded { get; set; }
    public string? Output { get; set; }
}
