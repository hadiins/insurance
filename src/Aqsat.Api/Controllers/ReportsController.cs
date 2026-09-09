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
using System.Globalization;

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
            GroupByJalaliMonth(items),
            totals.OperatingExpense,
            totals.MarketerCommissionPaid));
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
        WriteRow("پورسانت پرداخت‌شدهٔ دوره", totals.MarketerCommissionPaid);
        WriteRow("سوخت نکول", totals.DefaultWriteOffExpense);
        WriteRow("هزینه‌های عملیاتی", totals.OperatingExpense);
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
                // A cancelled policy never earned its commission — leaving it in would count
                // income the insurer claws back. PendingConfirmation stays: the policy is real,
                // the agent's sign-off flag is an internal worklist detail.
                .Where(p => p.Status != PolicyStatus.Cancelled)
                .Include(p => p.InsuranceLine)
                .Include(p => p.Marketer)
                .ToListAsync(ct);

            items.AddRange(policies.Select(p => new LineItem(
                p.InsuranceLineId, p.InsuranceLine.NameFa, p.MarketerId, p.Marketer?.FullName ?? "بدون بازاریاب", p.IssueDate,
                new PnlTotals(p.AgencyCommissionAmount ?? 0, p.ServiceFee, 0, 0))));
        }
        else
        {
            var allocations = await dbContext.PaymentAllocations
                .AsNoTracking()
                .Where(a => a.Payment.PaidOn >= from && a.Payment.PaidOn <= to)
                .Where(a => !a.Payment.IsDeleted && a.Installment.Policy.Status != PolicyStatus.Cancelled)
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

            // Down payments are recorded as their own Payment (PoliciesController.GenerateScheduleAsync)
            // but deliberately carry no PaymentAllocation — they aren't collected against any specific
            // installment, since they were already subtracted from FinancedAmount before the
            // installments were sized. Recognized here the same proportional way as an installment
            // payment, keyed off the policy the down-payment sentinel (InstallmentIdHint) points at.
            var downPayments = await dbContext.Payments
                .AsNoTracking()
                .Where(p => p.Method == PoliciesController.DownPaymentMethod && p.PaidOn >= from && p.PaidOn <= to)
                .Where(p => !p.IsDeleted)
                .ToListAsync(ct);

            if (downPayments.Count > 0)
            {
                var policyIds = downPayments.Select(p => p.InstallmentIdHint).ToList();
                var policiesById = await dbContext.Policies
                    .AsNoTracking()
                    .Where(p => policyIds.Contains(p.Id) && p.Status != PolicyStatus.Cancelled)
                    .Include(p => p.InsuranceLine)
                    .Include(p => p.Marketer)
                    .ToDictionaryAsync(p => p.Id, ct);

                items.AddRange(downPayments
                    .Where(p => policiesById.ContainsKey(p.InstallmentIdHint))
                    .Select(p =>
                    {
                        var policy = policiesById[p.InstallmentIdHint];
                        var ratio = policy.TotalReceivable > 0 ? p.Amount / policy.TotalReceivable : 0;
                        return new LineItem(
                            policy.InsuranceLineId, policy.InsuranceLine.NameFa, policy.MarketerId, policy.Marketer?.FullName ?? "بدون بازاریاب",
                            p.PaidOn, new PnlTotals((policy.AgencyCommissionAmount ?? 0) * ratio, policy.ServiceFee * ratio, 0, 0));
                    }));
            }
        }

        var commissionEntries = await dbContext.CommissionEntries
            .AsNoTracking()
            .Where(c => (c.Status == CommissionStatus.Payable || c.Status == CommissionStatus.Paid)
                && c.EligibleAt != null && c.EligibleAt.Value.Date >= from.ToDateTime(TimeOnly.MinValue)
                && c.EligibleAt.Value.Date <= to.ToDateTime(TimeOnly.MinValue))
            // Same cancelled-policy exclusion as the income side — a cancelled policy must not
            // carry commission expense either, or the P&L nets to a phantom loss.
            .Where(c => c.Policy.Status != PolicyStatus.Cancelled)
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
            .Where(i => i.Policy.Status != PolicyStatus.Cancelled)
            .Include(i => i.Policy).ThenInclude(p => p.InsuranceLine)
            .Include(i => i.Policy).ThenInclude(p => p.Marketer)
            .ToListAsync(ct);

        items.AddRange(overdueInstallments
            .Where(i => today.DayNumber - i.SettlementDeadline.DayNumber >= writeOffThresholdDays)
            .Select(i => new LineItem(
                i.Policy.InsuranceLineId, i.Policy.InsuranceLine.NameFa, i.Policy.MarketerId, i.Policy.Marketer?.FullName ?? "بدون بازاریاب",
                i.SettlementDeadline, new PnlTotals(0, 0, 0, i.Balance))));

        // Stage 7/7 — the agency's own operating costs, a real third expense line (never derived,
        // unlike default write-off). Not tied to any InsuranceLine/Marketer, so both fall back to
        // the same "بدون ..." grouping the rest of this method already uses for unassigned rows.
        var expenses = await dbContext.Expenses
            .AsNoTracking()
            .Where(e => e.Date >= from && e.Date <= to)
            .ToListAsync(ct);

        items.AddRange(expenses.Select(e => new LineItem(
            null, "بدون رشته", null, "بدون بازاریاب", e.Date, new PnlTotals(0, 0, 0, 0, e.Amount))));

        // Informational, not an expense — the expense itself is already recognized at EligibleAt
        // above; this is the cash actually handed over (CommissionPayout) within the period, so the
        // two lines can legitimately differ when a batch is paid late/early.
        var payouts = await dbContext.CommissionPayouts
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.PaidOn >= from && p.PaidOn <= to)
            .ToListAsync(ct);

        items.AddRange(payouts.Select(p => new LineItem(
            null, "بدون رشته", null, "بدون بازاریاب", p.PaidOn, new PnlTotals(0, 0, 0, 0, 0, p.Amount))));

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
                    totals.MarketerCommissionExpense, totals.DefaultWriteOffExpense, totals.TotalIncome, totals.TotalExpense, totals.NetProfit,
                    totals.OperatingExpense);
            })
            .OrderByDescending(r => r.NetProfit)
            .ToList();

    private static readonly string[] JalaliMonthNames =
        ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];
    private static readonly PersianCalendar Persian = new();

    /// <summary>Monthly trend bucketed by Jalali months, in chronological order (a trend chart must
    /// not arrive sorted by profit), with gap months between the first and last active month filled
    /// with zeros so a quiet month reads as zero, not as missing data.</summary>
    private static IReadOnlyList<PnlBreakdownRow> GroupByJalaliMonth(List<LineItem> items)
    {
        var buckets = new Dictionary<int, PnlTotals>();
        foreach (var item in items)
        {
            var date = item.EventDate.ToDateTime(TimeOnly.MinValue);
            var key = Persian.GetYear(date) * 12 + Persian.GetMonth(date) - 1;
            buckets[key] = buckets.TryGetValue(key, out var existing) ? existing + item.Totals : item.Totals;
        }
        if (buckets.Count == 0)
        {
            return [];
        }

        var rows = new List<PnlBreakdownRow>();
        for (var key = buckets.Keys.Min(); key <= buckets.Keys.Max(); key++)
        {
            var totals = buckets.TryGetValue(key, out var bucket) ? bucket : default;
            rows.Add(new PnlBreakdownRow(
                key.ToString(), $"{JalaliMonthNames[key % 12]} {key / 12}", totals.AgencyCommissionIncome, totals.ServiceFeeIncome,
                totals.MarketerCommissionExpense, totals.DefaultWriteOffExpense, totals.TotalIncome, totals.TotalExpense, totals.NetProfit,
                totals.OperatingExpense));
        }
        return rows;
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });

    private sealed record LineItem(Guid? InsuranceLineId, string LineLabel, Guid? MarketerId, string MarketerLabel, DateOnly EventDate, PnlTotals Totals);
}
