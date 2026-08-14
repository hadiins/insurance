using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>A failed row never aborts the batch — per-row errors live here.</summary>
public class ImportRow : AgencyOwnedEntity
{
    public Guid ImportBatchId { get; set; }
    public ImportBatch ImportBatch { get; set; } = default!;

    public int RowNumber { get; set; }
    public ImportRowStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
}
