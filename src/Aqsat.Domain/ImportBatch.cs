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

    public ICollection<ImportRow> Rows { get; set; } = new List<ImportRow>();
}
