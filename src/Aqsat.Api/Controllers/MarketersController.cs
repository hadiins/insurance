using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
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
public sealed class MarketersController(
    AppDbContext dbContext, ICurrentUserContext currentUser, IPasswordHasher passwordHasher) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MarketerDto>>> List(CancellationToken ct)
    {
        var marketers = await dbContext.Marketers
            .AsNoTracking()
            .OrderBy(m => m.FullName)
            .Select(m => new MarketerDto(m.Id, m.FullName, m.Mobile, m.Type.ToString(), m.IsActive, m.AppUserId,
                m.AppUser != null ? m.AppUser.FullName : null,
                m.AppUser != null ? m.AppUser.Mobile : null))
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

        return Ok(new MarketerDto(marketer.Id, marketer.FullName, marketer.Mobile, marketer.Type.ToString(), marketer.IsActive, marketer.AppUserId, null, null));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MarketerDto>> Update(Guid id, UpdateMarketerRequest request, CancellationToken ct)
    {
        var marketer = await dbContext.Marketers.Include(m => m.AppUser).FirstOrDefaultAsync(m => m.Id == id, ct);
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

        return Ok(new MarketerDto(marketer.Id, marketer.FullName, marketer.Mobile, marketer.Type.ToString(), marketer.IsActive, marketer.AppUserId,
            marketer.AppUser?.FullName, marketer.AppUser?.Mobile));
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

        // The same batch as an actual money outflow — without this row no cash ever left a
        // CashBox/BankAccount and the balance/movement views (CashFlowController) missed payouts.
        dbContext.CommissionPayouts.Add(new CommissionPayout
        {
            AgencyId = currentUser.ActiveOrganizationId,
            MarketerId = id,
            PaymentBatchId = batchId,
            Amount = entries.Sum(e => e.Amount),
            PaidOn = request.PaidOn ?? DateOnly.FromDateTime(now.UtcDateTime),
            MethodType = request.MethodType,
            CashBoxId = request.CashBoxId,
            BankAccountId = request.BankAccountId,
            ReferenceNo = string.IsNullOrWhiteSpace(request.ReferenceNo) ? null : request.ReferenceNo.Trim(),
        });

        await dbContext.SaveChangesAsync(ct);

        return Ok(new PayCommissionsResultDto(batchId, entries.Count, entries.Sum(e => e.Amount)));
    }

    /// <summary>Gives a marketer panel access in one step: creates the AppUser login if the mobile
    /// is unseen (password only matters then — an existing user keeps their own), grants the seeded
    /// "بازاریاب" role in this agency, and links it via Marketer.AppUserId — the exact link
    /// MarketerPanelController resolves the session's marketer by. Same one-user-one-marketer rule
    /// Create enforces above.</summary>
    [HttpPost("{id:guid}/panel-access")]
    public async Task<ActionResult<MarketerDto>> GrantPanelAccess(Guid id, CreateMarketerPanelAccessRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Mobile))
        {
            return ValidationProblem("شمارهٔ همراه الزامی است.");
        }

        var marketer = await dbContext.Marketers.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (marketer is null)
        {
            return NotFound();
        }

        if (marketer.AppUserId is not null)
        {
            return ValidationProblem("این بازاریاب قبلاً حساب پنل دارد.");
        }

        var mobile = request.Mobile.Trim();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Mobile == mobile && !u.IsDeleted, ct);

        if (user is null)
        {
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                return ValidationProblem("برای کاربر جدید رمز عبور الزامی است.");
            }

            // Same minimum AuthController.ChangePassword and UsersController.Create enforce.
            if (request.Password.Length < 8)
            {
                return ValidationProblem("رمز عبور باید حداقل ۸ کاراکتر باشد.");
            }

            user = new AppUser
            {
                FullName = (string.IsNullOrWhiteSpace(request.FullName) ? marketer.FullName : request.FullName).Trim(),
                Mobile = mobile,
                PasswordHash = passwordHasher.Hash(request.Password),
                IsActive = true,
            };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(ct);
        }
        else
        {
            var alreadyLinked = await dbContext.Marketers.AsNoTracking().AnyAsync(m => m.AppUserId == user.Id, ct);
            if (alreadyLinked)
            {
                return ValidationProblem("این کاربر قبلاً به یک بازاریاب دیگر متصل است.");
            }
        }

        var marketerRole = await MarketerRoleSeeder.EnsureSeededAsync(dbContext, ct);
        var alreadyMember = await dbContext.UserOrgRoles.AnyAsync(
            m => m.UserId == user.Id && m.OrganizationId == marketer.AgencyId && m.RoleId == marketerRole.Id && !m.IsDeleted, ct);
        if (!alreadyMember)
        {
            // A user who is already staff here keeps that membership — the marketer role is added
            // alongside it, and revoking panel access below only ever removes the marketer one.
            dbContext.UserOrgRoles.Add(new UserOrgRole
            {
                UserId = user.Id,
                OrganizationId = marketer.AgencyId,
                RoleId = marketerRole.Id,
            });
        }

        marketer.AppUserId = user.Id;
        await dbContext.SaveChangesAsync(ct);

        return Ok(new MarketerDto(marketer.Id, marketer.FullName, marketer.Mobile, marketer.Type.ToString(), marketer.IsActive, marketer.AppUserId,
            user.FullName, user.Mobile));
    }

    /// <summary>Unlinks the panel login: Marketer.AppUserId is cleared (the panel no longer resolves
    /// this user) and the user's seeded-marketer-role membership in this agency is soft-deleted —
    /// never any other role's membership.</summary>
    [HttpDelete("{id:guid}/panel-access")]
    public async Task<ActionResult<MarketerDto>> RevokePanelAccess(Guid id, CancellationToken ct)
    {
        var marketer = await dbContext.Marketers.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (marketer is null)
        {
            return NotFound();
        }

        if (marketer.AppUserId is not { } userId)
        {
            return ValidationProblem("این بازاریاب حساب پنل ندارد.");
        }

        var marketerRole = await MarketerRoleSeeder.EnsureSeededAsync(dbContext, ct);
        var memberships = await dbContext.UserOrgRoles
            .Where(m => m.UserId == userId && m.OrganizationId == marketer.AgencyId && !m.IsDeleted)
            .ToListAsync(ct);
        foreach (var membership in memberships.Where(m => m.RoleId == marketerRole.Id))
        {
            membership.IsDeleted = true;
            membership.DeletedAt = DateTimeOffset.UtcNow;
        }

        marketer.AppUserId = null;
        await dbContext.SaveChangesAsync(ct);

        return Ok(new MarketerDto(marketer.Id, marketer.FullName, marketer.Mobile, marketer.Type.ToString(), marketer.IsActive, marketer.AppUserId, null, null));
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
