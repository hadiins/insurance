namespace Aqsat.Application.Import;

public sealed record ImportPreviewColumn(string Header, DetectedDateFormat DateFormat, DetectedCurrencyScale CurrencyScale);

public sealed record ImportPreview(
    IReadOnlyList<ImportPreviewColumn> Columns,
    IReadOnlyList<IReadOnlyList<string>> SampleRows,
    IReadOnlyList<ImportTargetField> TargetFields);

public sealed record ImportCommitReport(Guid BatchId, int NewCount, int DuplicateCount, int FailedCount);
