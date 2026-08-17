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
