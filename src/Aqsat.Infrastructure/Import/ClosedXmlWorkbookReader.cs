using Aqsat.Application.Common;
using ClosedXML.Excel;

namespace Aqsat.Infrastructure.Import;

public sealed class ClosedXmlWorkbookReader : IWorkbookReader
{
    /// <summary>A 20 MB xlsx can legitimately decompress to hundreds of thousands of rows — reading
    /// it all into string lists would exhaust the API container's memory on a single request.
    /// Real cartable/Fanavaran exports are a few hundred rows; anything beyond this cap is treated
    /// as a wrong or hostile file, not as data to import.</summary>
    private const int MaxDataRowsPerSheet = 5_000;

    public RawSheet ReadFirstSheet(Stream fileStream)
    {
        using var workbook = new XLWorkbook(fileStream);
        return ReadWorksheet(workbook.Worksheets.First());
    }

    public RawSheet ReadSheet(Stream fileStream, string sheetName)
    {
        using var workbook = new XLWorkbook(fileStream);
        if (!workbook.TryGetWorksheet(sheetName, out var worksheet))
        {
            throw new InvalidOperationException($"شیت «{sheetName}» در فایل یافت نشد.");
        }

        return ReadWorksheet(worksheet);
    }

    private static RawSheet ReadWorksheet(IXLWorksheet worksheet)
    {
        var usedRange = worksheet.RangeUsed();
        if (usedRange is null)
        {
            return new RawSheet([], []);
        }

        // RowsUsed() skips rows that are entirely blank — the reported row number in later
        // validation errors is this list's position, which can drift from the literal Excel row
        // number if a real file has a blank row in the middle of its data. Acceptable for Phase 1;
        // real Fanavaran/cartable exports don't do this.
        var rows = usedRange.RowsUsed().ToList();
        if (rows.Count == 0)
        {
            return new RawSheet([], []);
        }

        if (rows.Count - 1 > MaxDataRowsPerSheet)
        {
            throw new InvalidOperationException(
                $"تعداد ردیفهای فایل ({rows.Count - 1}) بیش از حد مجاز ({MaxDataRowsPerSheet:N0}) است.");
        }

        var firstColumn = usedRange.FirstColumn().ColumnNumber();
        var lastColumn = usedRange.LastColumn().ColumnNumber();

        var headers = new List<string>();
        for (var col = firstColumn; col <= lastColumn; col++)
        {
            headers.Add(rows[0].Cell(col).GetString().Trim());
        }

        var dataRows = new List<IReadOnlyList<string>>();
        foreach (var row in rows.Skip(1))
        {
            var values = new List<string>();
            for (var col = firstColumn; col <= lastColumn; col++)
            {
                values.Add(CellToString(row.Cell(col)));
            }

            dataRows.Add(values);
        }

        return new RawSheet(headers, dataRows);
    }

    private static string CellToString(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        return cell.DataType switch
        {
            XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd"),
            XLDataType.Number => cell.GetDouble().ToString("0.####################"),
            _ => cell.GetString().Trim(),
        };
    }
}
