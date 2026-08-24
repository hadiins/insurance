namespace Aqsat.Api.Contracts;

/// <summary>docs/TASK-24-POLICY-NUMBER.md §3 — "این یک ابزار تطبیق رایگان با فناوران است، بدون
/// هیچ API": every serial from 1 up to the highest one seen this year that has no matching policy
/// is presumably issued in Fanavaran but never entered here.</summary>
public sealed record MissingSerialsReportDto(
    int Year, string RangeStart, string? RangeEnd, int RegisteredCount, int MissingCount, IReadOnlyList<string> Missing);
