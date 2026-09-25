using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>Every import is recorded — file name, hash, row counts, provenance for every imported row.</summary>
public class ImportBatch : AgencyOwnedEntity
{
    public string FileName { get; set; } = default!;
    public string FileHash { get; set; } = default!;

    public int NewCount { get; set; }
    public int DuplicateCount { get; set; }
    public int FailedCount { get; set; }

    /// <summary>When this batch was committed — backs the «آخرین همگام‌سازی» line. Nullable because
    /// batches created before this column existed have no honest value to report; the UI shows
    /// «نامشخص» rather than inventing a time.</summary>
    public DateTimeOffset? CreatedAtUtc { get; set; }

    public ICollection<ImportRow> Rows { get; set; } = new List<ImportRow>();
}
