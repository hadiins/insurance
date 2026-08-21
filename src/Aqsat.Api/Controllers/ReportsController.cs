using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Application.Reports;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASKS.md Task 15 ⭐ — the feature agents ranked first. docs/PHASE-1-SPEC.md §3.6, verbatim:
/// درآمد (AgencyCommissionAmount + ServiceFee) − هزینه (marketer commission + default write-off).
/// "P&amp;L is not accounting" (CLAUDE.md) — no ledger, just arithmetic over data already in the
/// system. Accrual recognises income at issuance; cash recognises it at collection, prorated per
/// payment by how much of the policy's TotalReceivable that payment represents — the same
/// proportional-slicing idea CommissionGenerator already uses, applied to the other side of the ledger.
/// </summary>
[ApiController]
[Route("api/reports/pnl")]
[Authorize(Policy = Permissions.FinanceRead)]
public sealed class ReportsController(AppDbContext dbContext, TimeProvider timeProvider, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PnlResultDto>> Get(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] string basis,
        [FromQuery] int? writeOffThresholdDays, CancellationToken ct)
    {
        if (to < from)
        {
            return ValidationProblem("بازهٔ تاریخ نامعتبر است.");
        }

        if (!Enum.TryParse<PnlBasis>(basis, ignoreCase: true, out var pnlBasis))
        {
            return ValidationProblem("مبنای محاسبه باید Accrual یا Cash باشد.");
        }

        var (totals, items) = await ComputeAsync(from, to, pnlBasis, writeOffThresholdDays, ct);

        return Ok(new PnlResultDto(
            totals.AgencyCommissionIncome, totals.ServiceFeeIncome, totals.MarketerCommissionExpense, totals.DefaultWriteOffExpense,
            totals.TotalIncome, totals.TotalExpense, totals.NetProfit,
            GroupBy(items, i => (i.InsuranceLineId?.ToString() ?? "none", i.LineLabel)),
            GroupBy(items, i => (i.MarketerId?.ToString() ?? "none", i.MarketerLabel)),
            GroupBy(items, i => (i.EventDate.ToString("yyyy-MM"), i.EventDate.ToString("yyyy-MM")))));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] string basis,
        [FromQuery] int? writeOffThresholdDays, CancellationToken ct)
    {
        if (!Enum.TryParse<PnlBasis>(basis, ignoreCase: true, out var pnlBasis))
        {
            return ValidationProblem("مبنای محاسبه باید Accrual یا Cash باشد.");
        }

        var (totals, items) = await ComputeAsync(from, to, pnlBasis, writeOffThresholdDays, ct);

        using var workbook = new XLWorkbook();
        var summary = workbook.Worksheets.Add("سود و زیان");
        summary.RightToLeft = true;
        var row = 1;
        void WriteRow(string label, decimal amount)
        {
            summary.Cell(row, 1).Value = label;
            summary.Cell(row, 2).Value = amount;
            row++;
        }

        WriteRow("کارمزد از شرکت بیمه", totals.AgencyCommissionIncome);
        WriteRow("کارمزد خدمات", totals.ServiceFeeIncome);
        WriteRow("جمع درآمد", totals.TotalIncome);
        row++;
        WriteRow("پورسانت بازاریاب", totals.MarketerCommissionExpense);
        WriteRow("سوخت نکول", totals.DefaultWriteOffExpense);
        WriteRow("جمع هزینه", totals.TotalExpense);
        row++;
        WriteRow("سود خالص", totals.NetProfit);

        var byLine = workbook.Worksheets.Add("به‌تفکیک رشته");
        byLine.RightToLeft = true;
        WriteBreakdownSheet(byLine, GroupBy(items, i => (i.InsuranceLineId?.ToString() ?? "none", i.LineLabel)));

        var byMarketer = workbook.Worksheets.Add("به‌تفکیک بازاریاب");
        byMarketer.RightToLeft = true;
        WriteBreakdownSheet(byMarketer, GroupBy(items, i => (i.MarketerId?.ToString() ?? "none", i.MarketerLabel)));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"pnl-{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx");
    }

    private static void WriteBreakdownSheet(IXLWorksheet sheet, IReadOnlyList<PnlBreakdownRow> rows)
    {
        sheet.Cell(1, 1).Value = "برچسب";
        sheet.Cell(1, 2).Value = "درآمد";
        sheet.Cell(1, 3).Value = "هزینه";
        sheet.Cell(1, 4).Value = "سود خالص";
        for (var i = 0; i < rows.Count; i++)
        {
            sheet.Cell(i + 2, 1).Value = rows[i].GroupLabel;
            sheet.Cell(i + 2, 2).Value = rows[i].TotalIncome;
            sheet.Cell(i + 2, 3).Value = rows[i].TotalExpense;
            sheet.Cell(i + 2, 4).Value = rows[i].NetProfit;
        }
    }

    private async Task<(PnlTotals Totals, List<LineItem> Items)> ComputeAsync(
        DateOnly from, DateOnly to, PnlBasis basis, int? writeOffThresholdDaysOverride, CancellationToken ct)
    {
        var items = new List<LineItem>();

        if (basis == PnlBasis.Accrual)
        {
            var policies = await dbContext.Policies
                .AsNoTracking()
                .Where(p => p.IssueDate >= from && p.IssueDate <= to)
                .Include(p => p.InsuranceLine)
                .Include(p => p.Marketer)
                .ToListAsync(ct);

            items.AddRange(policies.Select(p => new LineItem(
                p.InsuranceLineId, p.InsuranceLine.NameFa, p.MarketerId, p.Marketer?.FullName ?? "بدون بازاریاب", p.IssueDate,
                new PnlTotals(p.AgencyCommissionAmount ?? 0, p.ServiceFee, 0, 0))));
        }
        else
        {
            // Known limitation: DownPayment is a plain field on Policy, not a tracked Payment/
            // PaymentAllocation (docs/TASKS.md Task 8's design) — so a down payment's collection
            // never appears here. Cash-basis income for a policy therefore only starts accruing
            // once its first *installment* payment is recorded; the down-payment share of
            // ServiceFee/AgencyCommission is not recognised under cash basis in this pass.
            var allocations = await dbContext.PaymentAllocations
                .AsNoTracking()
                .Where(a => a.Payment.PaidOn >= from && a.Payment.PaidOn <= to)
                .Include(a => a.Payment)
                .Include(a => a.Installment).ThenInclude(i => i.Policy).ThenInclude(p => p.InsuranceLine)
                .Include(a => a.Installment).ThenInclude(i => i.Policy).ThenInclude(p => p.Marketer)
                .ToListAsync(ct);

            items.AddRange(allocations.Select(a =>
            {
                var policy = a.Installment.Policy;
                var ratio = policy.TotalReceivable > 0 ? a.Amount / policy.TotalReceivable : 0;
                return new LineItem(
                    policy.InsuranceLineId, policy.InsuranceLine.NameFa, policy.MarketerId, policy.Marketer?.FullName ?? "بدون بازاریاب",
                    a.Payment.PaidOn, new PnlTotals((policy.AgencyCommissionAmount ?? 0) * ratio, policy.ServiceFee * ratio, 0, 0));
            }));
        }

        var commissionEntries = await dbContext.CommissionEntries
            .AsNoTracking()
            .Where(c => (c.Status == CommissionStatus.Payable || c.Status == CommissionStatus.Paid)
                && c.EligibleAt != null && c.EligibleAt.Value.Date >= from.ToDateTime(TimeOnly.MinValue)
                && c.EligibleAt.Value.Date <= to.ToDateTime(TimeOnly.MinValue))
            .Include(c => c.Policy).ThenInclude(p => p.InsuranceLine)
            .Include(c => c.Marketer)
            .ToListAsync(ct);

        items.AddRange(commissionEntries.Select(c => new LineItem(
            c.Policy.InsuranceLineId, c.Policy.InsuranceLine.NameFa, c.MarketerId, c.Marketer.FullName,
            DateOnly.FromDateTime(c.EligibleAt!.Value.Date), new PnlTotals(0, 0, c.Amount, 0))));

        // OrgSettings is not RLS-scoped (it has no AgencyId column — it's keyed on OrganizationId
        // directly), so an unfiltered query here would return an arbitrary agency's row.
        var orgSettings = await dbContext.OrgSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == currentUser.ActiveOrganizationId, ct);
        var writeOffThresholdDays = writeOffThresholdDaysOverride ?? orgSettings?.DefaultWriteOffDays ?? 30;
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var overdueInstallments = await dbContext.Installments
            .AsNoTracking()
            .Where(i => i.Status != InstallmentStatus.Settled && i.SettlementDeadline >= from && i.SettlementDeadline <= to)
            .Include(i => i.Policy).ThenInclude(p => p.InsuranceLine)
            .Include(i => i.Policy).ThenInclude(p => p.Marketer)
            .ToListAsync(ct);

        items.AddRange(overdueInstallments
            .Where(i => today.DayNumber - i.SettlementDeadline.DayNumber >= writeOffThresholdDays)
            .Select(i => new LineItem(
                i.Policy.InsuranceLineId, i.Policy.InsuranceLine.NameFa, i.Policy.MarketerId, i.Policy.Marketer?.FullName ?? "بدون بازاریاب",
                i.SettlementDeadline, new PnlTotals(0, 0, 0, i.Balance))));

        var totals = items.Aggregate(default(PnlTotals), (acc, item) => acc + item.Totals);
        return (totals, items);
    }

    private static IReadOnlyList<PnlBreakdownRow> GroupBy(IEnumerable<LineItem> items, Func<LineItem, (string Key, string Label)> keySelector) =>
        items
            .GroupBy(keySelector)
            .Select(g =>
            {
                var totals = g.Aggregate(default(PnlTotals), (acc, item) => acc + item.Totals);
                return new PnlBreakdownRow(
                    g.Key.Key, g.Key.Label, totals.AgencyCommissionIncome, totals.ServiceFeeIncome,
                    totals.MarketerCommissionExpense, totals.DefaultWriteOffExpense, totals.TotalIncome, totals.TotalExpense, totals.NetProfit);
            })
            .OrderByDescending(r => r.NetProfit)
            .ToList();

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });

    private sealed record LineItem(Guid? InsuranceLineId, string LineLabel, Guid? MarketerId, string MarketerLabel, DateOnly EventDate, PnlTotals Totals);
}
