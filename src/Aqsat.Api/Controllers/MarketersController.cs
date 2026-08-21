using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASKS.md Task 13 — agency-side marketer management. `Marketer.Manage` is a distinct
/// permission from `Policy.Write` on purpose: an office manager might administer marketers without
/// being able to issue policies, or vice versa.
/// </summary>
[ApiController]
[Route("api/marketers")]
[Authorize(Policy = Permissions.MarketerManage)]
public sealed class MarketersController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MarketerDto>>> List(CancellationToken ct)
    {
        var marketers = await dbContext.Marketers
            .AsNoTracking()
            .OrderBy(m => m.FullName)
            .Select(m => new MarketerDto(m.Id, m.FullName, m.Mobile, m.Type.ToString(), m.IsActive, m.AppUserId))
            .ToListAsync(ct);

        return Ok(marketers);
    }

    [HttpPost]
    public async Task<ActionResult<MarketerDto>> Create(CreateMarketerRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Mobile))
        {
            return ValidationProblem("نام و شمارهٔ همراه بازاریاب الزامی است.");
        }

        if (!Enum.TryParse<MarketerType>(request.Type, out var type))
        {
            return ValidationProblem("نوع بازاریاب نامعتبر است.");
        }

        if (request.AppUserId is { } appUserId)
        {
            var alreadyLinked = await dbContext.Marketers.AsNoTracking().AnyAsync(m => m.AppUserId == appUserId, ct);
            if (alreadyLinked)
            {
                return ValidationProblem("این کاربر قبلاً به یک بازاریاب دیگر متصل است.");
            }
        }

        var marketer = new Marketer
        {
            AgencyId = currentUser.ActiveOrganizationId,
            FullName = request.FullName.Trim(),
            Mobile = request.Mobile.Trim(),
            Type = type,
            AppUserId = request.AppUserId,
            IsActive = true,
        };

        if (!string.IsNullOrWhiteSpace(request.NationalId))
        {
            marketer.NationalId = request.NationalId;
        }

        dbContext.Marketers.Add(marketer);
        await dbContext.SaveChangesAsync(ct);

        return Ok(new MarketerDto(marketer.Id, marketer.FullName, marketer.Mobile, marketer.Type.ToString(), marketer.IsActive, marketer.AppUserId));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MarketerDto>> Update(Guid id, UpdateMarketerRequest request, CancellationToken ct)
    {
        var marketer = await dbContext.Marketers.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (marketer is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Mobile))
        {
            return ValidationProblem("نام و شمارهٔ همراه بازاریاب الزامی است.");
        }

        marketer.FullName = request.FullName.Trim();
        marketer.Mobile = request.Mobile.Trim();
        marketer.IsActive = request.IsActive;
        await dbContext.SaveChangesAsync(ct);

        return Ok(new MarketerDto(marketer.Id, marketer.FullName, marketer.Mobile, marketer.Type.ToString(), marketer.IsActive, marketer.AppUserId));
    }

    [HttpGet("{id:guid}/rates")]
    public async Task<ActionResult<IReadOnlyList<MarketerRateDto>>> Rates(Guid id, CancellationToken ct)
    {
        var rates = await dbContext.MarketerRates
            .AsNoTracking()
            .Where(r => r.MarketerId == id)
            .Include(r => r.InsuranceLine)
            .OrderByDescending(r => r.EffectiveFrom)
            .Select(r => new MarketerRateDto(r.Id, r.InsuranceLineId, r.InsuranceLine.NameFa, r.RatePercent, r.EffectiveFrom, r.EffectiveTo))
            .ToListAsync(ct);

        return Ok(rates);
    }

    [HttpPost("{id:guid}/rates")]
    public async Task<ActionResult<MarketerRateDto>> SetRate(Guid id, SetMarketerRateRequest request, CancellationToken ct)
    {
        var marketerExists = await dbContext.Marketers.AsNoTracking().AnyAsync(m => m.Id == id, ct);
        if (!marketerExists)
        {
            return NotFound();
        }

        if (request.RatePercent <= 0)
        {
            return ValidationProblem("درصد پورسانت باید مثبت باشد.");
        }

        // Never update a prior rate row in place — close it, then insert the new one, so
        // CommissionEntry.RatePercent locked at past issuances stays meaningful.
        var previous = await dbContext.MarketerRates
            .Where(r => r.MarketerId == id && r.InsuranceLineId == request.InsuranceLineId && r.EffectiveTo == null)
            .FirstOrDefaultAsync(ct);
        if (previous is not null)
        {
            previous.EffectiveTo = request.EffectiveFrom.AddDays(-1);
        }

        var rate = new MarketerRate
        {
            AgencyId = currentUser.ActiveOrganizationId,
            MarketerId = id,
            InsuranceLineId = request.InsuranceLineId,
            RatePercent = request.RatePercent,
            EffectiveFrom = request.EffectiveFrom,
        };
        dbContext.MarketerRates.Add(rate);
        await dbContext.SaveChangesAsync(ct);

        var lineName = await dbContext.InsuranceLines.AsNoTracking()
            .Where(l => l.Id == request.InsuranceLineId).Select(l => l.NameFa).FirstAsync(ct);

        return Ok(new MarketerRateDto(rate.Id, rate.InsuranceLineId, lineName, rate.RatePercent, rate.EffectiveFrom, rate.EffectiveTo));
    }

    [HttpGet("{id:guid}/commissions")]
    public async Task<ActionResult<CommissionSummaryDto>> Commissions(Guid id, CancellationToken ct)
    {
        var entries = await dbContext.CommissionEntries
            .AsNoTracking()
            .Where(c => c.MarketerId == id)
            .Include(c => c.Policy)
            .Include(c => c.Installment)
            .OrderByDescending(c => c.EligibleAt)
            .ToListAsync(ct);

        var dtos = entries
            .Select(c => new CommissionEntryDto(
                c.Id, c.PolicyId, c.Policy.PolicyNumber, c.Installment?.SeqNo, c.Amount, c.Status.ToString(), c.EligibleAt, c.PaidAt))
            .ToList();

        return Ok(new CommissionSummaryDto(
            entries.Where(c => c.Status == CommissionStatus.Pending).Sum(c => c.Amount),
            entries.Where(c => c.Status == CommissionStatus.Payable).Sum(c => c.Amount),
            entries.Where(c => c.Status == CommissionStatus.Paid).Sum(c => c.Amount),
            dtos));
    }

    /// <summary>Groups selected payable entries into one payment batch — docs/TASKS.md Task 13's
    /// "payment batches". Only entries already Payable can be paid; anything else in the request is
    /// silently skipped rather than erroring the whole batch, matching this codebase's established
    /// per-item isolation pattern.</summary>
    [HttpPost("{id:guid}/commissions/pay")]
    public async Task<ActionResult<PayCommissionsResultDto>> PayCommissions(Guid id, PayCommissionsRequest request, CancellationToken ct)
    {
        var entries = await dbContext.CommissionEntries
            .Where(c => c.MarketerId == id && request.CommissionEntryIds.Contains(c.Id) && c.Status == CommissionStatus.Payable)
            .ToListAsync(ct);

        if (entries.Count == 0)
        {
            return ValidationProblem("هیچ سهم قابل‌پرداختی در این انتخاب یافت نشد.");
        }

        var batchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in entries)
        {
            entry.Status = CommissionStatus.Paid;
            entry.PaidAt = now;
            entry.PaymentBatchId = batchId;
        }

        await dbContext.SaveChangesAsync(ct);

        return Ok(new PayCommissionsResultDto(batchId, entries.Count, entries.Sum(e => e.Amount)));
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
