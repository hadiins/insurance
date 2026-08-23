namespace Aqsat.Api.Contracts;

public sealed record ImportPreviewColumnDto(string Header, string DateFormat, string CurrencyScale);

public sealed record ImportTargetFieldDto(string Key, string Label, bool Required, string Type);

public sealed record ImportPreviewResponse(
    IReadOnlyList<ImportPreviewColumnDto> Columns,
    IReadOnlyList<IReadOnlyList<string>> SampleRows,
    IReadOnlyList<ImportTargetFieldDto> TargetFields);

public sealed record ColumnMappingRequest(string ImportType, Dictionary<string, string> Mapping);

public sealed record ColumnMappingResponse(Dictionary<string, string>? Mapping);

/// <summary>Sent as a JSON-encoded form field alongside the uploaded file — multipart/form-data
/// doesn't bind a Dictionary&lt;string,string&gt; cleanly on its own.</summary>
public sealed record ImportCommitMeta(string ImportType, Dictionary<string, string> Mapping, string DateFormat, bool AmountsAreInRials);

public sealed record ImportCommitResponse(Guid BatchId, int NewCount, int DuplicateCount, int FailedCount);

/// <summary>تاریخچهٔ ورود داده — every batch ever committed, most recent first (BizId order, since
/// ImportBatch carries no CreatedAt of its own).</summary>
public sealed record ImportBatchDto(Guid Id, string FileName, int NewCount, int DuplicateCount, int FailedCount);

/// <summary>رکوردهای ناسازگار — rows a commit could not place, with the reason (ImportRow.ErrorMessage
/// is composed at write time by ImportService, never reconstructed here).</summary>
public sealed record ImportMismatchRowDto(Guid Id, Guid ImportBatchId, string FileName, int RowNumber, string? ErrorMessage);
