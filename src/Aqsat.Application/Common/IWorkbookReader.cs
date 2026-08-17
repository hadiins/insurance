namespace Aqsat.Application.Common;

/// <summary>Result of reading a worksheet: header row plus every data row beneath it, as raw cell
/// text — no type coercion or column-mapping applied yet.</summary>
public sealed record RawSheet(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>Reads an uploaded spreadsheet (docs/PHASE-1-SPEC.md §4.3/§4.1). xlsx only in Phase 1 —
/// no csv, matching the Fanavaran/cartable exports this product actually receives.</summary>
public interface IWorkbookReader
{
    RawSheet ReadFirstSheet(Stream fileStream);

    /// <summary>Task 7 needs a specific sheet ("CarSalesBNVer") rather than whichever is first.
    /// Throws if no sheet with that name exists — never silently falls back to the first sheet,
    /// since that could quietly parse the wrong data.</summary>
    RawSheet ReadSheet(Stream fileStream, string sheetName);
}
