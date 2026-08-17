namespace Aqsat.Application.Common;

/// <summary>Result of reading the first worksheet of an uploaded file: header row plus every data
/// row beneath it, as raw cell text — no type coercion or column-mapping applied yet.</summary>
public sealed record RawSheet(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>Reads the generic import pipeline's uploaded spreadsheet (docs/PHASE-1-SPEC.md §4.3).
/// xlsx only in Phase 1 — no csv, matching the Fanavaran/cartable exports this product actually
/// receives.</summary>
public interface IWorkbookReader
{
    RawSheet ReadFirstSheet(Stream fileStream);
}
