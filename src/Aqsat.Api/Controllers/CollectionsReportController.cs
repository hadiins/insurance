using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASKS.md Task 18 — filtered reports with server-side paging and xlsx export, agent-requested.
/// Reads Installment directly, filtered on its (AgencyId, Status, SettlementDeadline) index — never a
/// heavy join across the operational tables CLAUDE.md warns about. A Hangfire-populated reporting
/// table is the documented next step if this ever needs to scale past what one agency's live table
/// can serve directly.
/// </summary>
[ApiController]
[Route("api/reports/collections")]
[Authorize(Policy = Permissions.ReportRead)]
public sealed class CollectionsReportController(AppDbContext dbContext, TimeProvider timeProvider, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CollectionsReportPageDto>> Get(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? lineId, [FromQuery] string? status,
        [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
    {
        if (to < from)
        {
            return ValidationProblem("بازهٔ تاریخ نامعتبر است.");
        }

        page = page <= 0 ? 1 : page;
        pageSize = pageSize is <= 0 or > 200 ? 50 : pageSize;

        var query = BuildFilteredQuery(from, to, lineId, status);

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(i => i.SettlementDeadline)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new CollectionsReportRow(
                i.Policy.PolicyNumber, i.Policy.Customer.FullName, i.Policy.InsuranceLine.NameFa, i.SeqNo,
                i.DueDate, i.SettlementDeadline, i.Amount, i.PaidAmount, i.Balance, i.Status.ToString()))
            .ToListAsync(ct);

        return Ok(new CollectionsReportPageDto(totalCount, rows));
    }

    [HttpGet("summary")]
    public async Task<ActionResult<CollectionsSummaryDto>> Summary(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? lineId, CancellationToken ct)
    {
        if (to < from)
        {
            return ValidationProblem("بازهٔ تاریخ نامعتبر است.");
        }

        var installments = await BuildFilteredQuery(from, to, lineId, status: null)
            .Select(i => new { i.Id, i.Amount, i.PaidAmount, i.Status, i.SettlementDeadline })
            .ToListAsync(ct);

        var orgSettings = await dbContext.OrgSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == currentUser.ActiveOrganizationId, ct);
        var writeOffThresholdDays = orgSettings?.DefaultWriteOffDays ?? 30;
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var settledIds = installments.Where(i => i.Status == InstallmentStatus.Settled).Select(i => i.Id).ToList();
        var lastPaymentByInstallment = await dbContext.PaymentAllocations.AsNoTracking()
            .Where(a => settledIds.Contains(a.InstallmentId))
            .GroupBy(a => a.InstallmentId)
            .Select(g => new { InstallmentId = g.Key, LastPaidOn = g.Max(a => a.Payment.PaidOn) })
            .ToDictionaryAsync(g => g.InstallmentId, g => g.LastPaidOn, ct);

        var settledOnTime = 0;
        var settledLate = 0;
        var open = 0;
        var writtenOff = 0;
        foreach (var i in installments)
        {
            if (i.Status == InstallmentStatus.Settled)
            {
                var lastPaidOn = lastPaymentByInstallment.GetValueOrDefault(i.Id);
                if (lastPaidOn != default && lastPaidOn <= i.SettlementDeadline)
                {
                    settledOnTime++;
                }
                else
                {
                    settledLate++;
                }
            }
            else if (today.DayNumber - i.SettlementDeadline.DayNumber >= writeOffThresholdDays)
            {
                writtenOff++;
            }
            else
            {
                open++;
            }
        }

        var totalCount = installments.Count;
        var onTimeRate = totalCount == 0 ? 0 : Math.Round(100m * settledOnTime / totalCount, 1);
        var defaultRate = totalCount == 0 ? 0 : Math.Round(100m * writtenOff / totalCount, 1);

        return Ok(new CollectionsSummaryDto(
            totalCount, installments.Sum(i => i.Amount), installments.Sum(i => i.PaidAmount),
            settledOnTime, settledLate, open, writtenOff, onTimeRate, defaultRate));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? lineId, [FromQuery] string? status, CancellationToken ct)
    {
        if (to < from)
        {
            return ValidationProblem("بازهٔ تاریخ نامعتبر است.");
        }

        var rows = await BuildFilteredQuery(from, to, lineId, status)
            .OrderBy(i => i.SettlementDeadline)
            .Select(i => new CollectionsReportRow(
                i.Policy.PolicyNumber, i.Policy.Customer.FullName, i.Policy.InsuranceLine.NameFa, i.SeqNo,
                i.DueDate, i.SettlementDeadline, i.Amount, i.PaidAmount, i.Balance, i.Status.ToString()))
            .ToListAsync(ct);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("وصولی‌ها");
        sheet.RightToLeft = true;
        string[] headers = ["بیمه‌نامه", "بیمه‌گذار", "رشته", "قسط", "سررسید", "مهلت تسویه", "مبلغ", "پرداخت‌شده", "مانده", "وضعیت"];
        for (var col = 0; col < headers.Length; col++)
        {
            sheet.Cell(1, col + 1).Value = headers[col];
        }

        for (var row = 0; row < rows.Count; row++)
        {
            var r = rows[row];
            var excelRow = row + 2;
            sheet.Cell(excelRow, 1).Value = r.PolicyNumber;
            sheet.Cell(excelRow, 2).Value = r.CustomerFullName;
            sheet.Cell(excelRow, 3).Value = r.InsuranceLineNameFa;
            sheet.Cell(excelRow, 4).Value = r.SeqNo;
            sheet.Cell(excelRow, 5).Value = r.DueDate.ToString("yyyy-MM-dd");
            sheet.Cell(excelRow, 6).Value = r.SettlementDeadline.ToString("yyyy-MM-dd");
            sheet.Cell(excelRow, 7).Value = r.Amount;
            sheet.Cell(excelRow, 8).Value = r.PaidAmount;
            sheet.Cell(excelRow, 9).Value = r.Balance;
            sheet.Cell(excelRow, 10).Value = r.Status;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"collections-{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx");
    }

    private IQueryable<Installment> BuildFilteredQuery(DateOnly from, DateOnly to, Guid? lineId, string? status)
    {
        var query = dbContext.Installments.AsNoTracking()
            .Where(i => i.SettlementDeadline >= from && i.SettlementDeadline <= to)
            .Include(i => i.Policy).ThenInclude(p => p.Customer)
            .Include(i => i.Policy).ThenInclude(p => p.InsuranceLine)
            .AsQueryable();

        if (lineId is { } line)
        {
            query = query.Where(i => i.Policy.InsuranceLineId == line);
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<InstallmentStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(i => i.Status == parsedStatus);
        }

        return query;
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
