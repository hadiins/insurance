using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Infrastructure.Persistence;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASK-24-POLICY-NUMBER.md §3 — "شماره‌های جا افتاده": a free reconciliation tool against
/// Fanavaran, no API involved. A gap between serial 1 and the highest one seen this year usually
/// means a policy exists there but was never entered into this system.
/// </summary>
[ApiController]
[Route("api/reports/missing-serials")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class MissingSerialsReportController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MissingSerialsReportDto>> Get([FromQuery] int year, CancellationToken ct)
    {
        var report = await BuildAsync(year, ct);
        return Ok(report);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int year, CancellationToken ct)
    {
        var report = await BuildAsync(year, ct);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add($"جا افتاده {year}");
        sheet.RightToLeft = true;
        sheet.Cell(1, 1).Value = "سریال غایب";
        for (var i = 0; i < report.Missing.Count; i++)
        {
            sheet.Cell(i + 2, 1).Value = report.Missing[i];
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"missing-serials-{year}.xlsx");
    }

    private async Task<MissingSerialsReportDto> BuildAsync(int year, CancellationToken ct)
    {
        var serials = await dbContext.Policies.AsNoTracking()
            .Where(p => p.PnIsParsed && p.PnYear == year && p.PnSerial != null)
            .Select(p => p.PnSerial!)
            .ToListAsync(ct);

        var numeric = serials
            .Select(s => (Serial: s, Numeric: int.TryParse(s, out var n) ? n : -1))
            .Where(x => x.Numeric >= 1)
            .ToList();

        if (numeric.Count == 0)
        {
            return new MissingSerialsReportDto(year, "000001", null, 0, 0, []);
        }

        var serialLength = numeric[0].Serial.Length;
        var maxSerial = numeric.Max(x => x.Numeric);
        var present = numeric.Select(x => x.Numeric).ToHashSet();

        var missing = new List<string>();
        for (var n = 1; n <= maxSerial; n++)
        {
            if (!present.Contains(n))
            {
                missing.Add(n.ToString().PadLeft(serialLength, '0'));
            }
        }

        return new MissingSerialsReportDto(
            year, "1".PadLeft(serialLength, '0'), maxSerial.ToString().PadLeft(serialLength, '0'),
            present.Count, missing.Count, missing);
    }
}
