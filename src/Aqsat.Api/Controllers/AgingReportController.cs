using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// «گزارش سنی معوقات» — the standard collections tool: every customer's open installments bucketed
/// by days past due (جاری / ۱-۳۰ / ۳۱-۶۰ / ۶۰+). Buckets hold the remaining Balance, never the
/// original Amount. "P&amp;L is not accounting" (CLAUDE.md): pure arithmetic over Installment rows
/// the system already holds, nothing new is recorded.
/// </summary>
[ApiController]
[Route("api/reports/aging")]
[Authorize(Policy = Permissions.FinanceRead)]
public sealed class AgingReportController(AppDbContext dbContext, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AgingReportDto>> Get(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var openInstallments = await dbContext.Installments
            .AsNoTracking()
            // Balance is [NotMapped] (Amount − PaidAmount), so the open test is spelled out —
            // EF cannot translate the computed property.
            .Where(i => i.Status != InstallmentStatus.Settled && i.Amount - i.PaidAmount > 0)
            .Where(i => i.Policy.Status != PolicyStatus.Cancelled)
            .Select(i => new
            {
                i.Policy.CustomerId,
                i.Policy.Customer.FullName,
                i.Policy.Customer.Mobile,
                i.DueDate,
                Balance = i.Amount - i.PaidAmount,
            })
            .ToListAsync(ct);

        var rows = openInstallments
            .GroupBy(i => new { i.CustomerId, i.FullName, i.Mobile })
            .Select(g =>
            {
                var buckets = g.ToList();
                return new AgingReportRow(
                    g.Key.CustomerId,
                    g.Key.FullName,
                    g.Key.Mobile,
                    OpenCount: buckets.Count,
                    CurrentAmount: buckets.Where(i => i.DueDate >= today).Sum(i => i.Balance),
                    Overdue1To30: buckets.Where(i => DaysOverdue(i.DueDate, today) is >= 1 and <= 30).Sum(i => i.Balance),
                    Overdue31To60: buckets.Where(i => DaysOverdue(i.DueDate, today) is >= 31 and <= 60).Sum(i => i.Balance),
                    Overdue60Plus: buckets.Where(i => DaysOverdue(i.DueDate, today) > 60).Sum(i => i.Balance),
                    TotalOpen: buckets.Sum(i => i.Balance),
                    OldestOverdueDueDate: buckets.Where(i => i.DueDate < today).Select(i => (DateOnly?)i.DueDate).Min());
            })
            // Worst debtors first: highest overdue total, then oldest due date as the tie-breaker.
            .OrderByDescending(r => r.Overdue1To30 + r.Overdue31To60 + r.Overdue60Plus)
            .ThenBy(r => r.OldestOverdueDueDate ?? DateOnly.MaxValue)
            .ToList();

        return Ok(new AgingReportDto(
            rows.Sum(r => r.CurrentAmount),
            rows.Sum(r => r.Overdue1To30),
            rows.Sum(r => r.Overdue31To60),
            rows.Sum(r => r.Overdue60Plus),
            rows.Sum(r => r.TotalOpen),
            rows));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var result = await Get(ct);
        if (result.Result is not OkObjectResult ok || ok.Value is not AgingReportDto dto)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("سنی معوقات");
        sheet.RightToLeft = true;
        sheet.Cell(1, 1).Value = "مشتری";
        sheet.Cell(1, 2).Value = "موبایل";
        sheet.Cell(1, 3).Value = "تعداد قسط باز";
        sheet.Cell(1, 4).Value = "جاری";
        sheet.Cell(1, 5).Value = "۱ تا ۳۰ روز";
        sheet.Cell(1, 6).Value = "۳۱ تا ۶۰ روز";
        sheet.Cell(1, 7).Value = "بیش از ۶۰ روز";
        sheet.Cell(1, 8).Value = "جمع بدهی";
        for (var i = 0; i < dto.Rows.Count; i++)
        {
            var r = dto.Rows[i];
            sheet.Cell(i + 2, 1).Value = r.CustomerFullName;
            sheet.Cell(i + 2, 2).Value = r.CustomerMobile;
            sheet.Cell(i + 2, 3).Value = r.OpenCount;
            sheet.Cell(i + 2, 4).Value = r.CurrentAmount;
            sheet.Cell(i + 2, 5).Value = r.Overdue1To30;
            sheet.Cell(i + 2, 6).Value = r.Overdue31To60;
            sheet.Cell(i + 2, 7).Value = r.Overdue60Plus;
            sheet.Cell(i + 2, 8).Value = r.TotalOpen;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"aging-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    private static int DaysOverdue(DateOnly dueDate, DateOnly today) => today.DayNumber - dueDate.DayNumber;
}
