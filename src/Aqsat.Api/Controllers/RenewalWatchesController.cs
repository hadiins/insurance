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
/// docs/TASKS.md Task 16 (niaz #4) — "the only part of the product that creates revenue rather than
/// reducing loss" (PHASE-1-SPEC.md §2.9). Covers both walk-in prospects with no policy here yet and
/// the agency's own policies approaching renewal (auto-watched by RenewalWatchJob).
/// </summary>
[ApiController]
[Route("api/renewal-watches")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class RenewalWatchesController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RenewalWatchDto>>> List([FromQuery] string? status, CancellationToken ct)
    {
        var query = dbContext.RenewalWatches.AsNoTracking()
            .Include(w => w.Customer)
            .Include(w => w.InsuranceLine)
            .Include(w => w.Marketer)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<RenewalWatchStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(w => w.Status == parsed);
        }

        var watches = await query
            .OrderBy(w => w.CurrentExpiryDate)
            .Select(w => ToDto(w))
            .ToListAsync(ct);

        return Ok(watches);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<ActionResult<RenewalWatchDto>> Create(CreateRenewalWatchRequest request, CancellationToken ct)
    {
        var hasCustomer = request.CustomerId is not null;
        var hasProspect = !string.IsNullOrWhiteSpace(request.ProspectName) && !string.IsNullOrWhiteSpace(request.ProspectMobile);
        if (hasCustomer == hasProspect)
        {
            return ValidationProblem("دقیقاً یکی از مشتری موجود یا نام و شمارهٔ همراه مشتری احتمالی را وارد کنید.");
        }

        if (request.NotifyDaysBefore <= 0)
        {
            return ValidationProblem("تعداد روزهای یادآوری باید مثبت باشد.");
        }

        if (hasCustomer)
        {
            // RLS-scoped: a Customer belonging to another agency simply won't be found here
            // (CLAUDE.md rule 11 — a FK to an invisible row does not throw, it silently succeeds,
            // so this existence check IS the scope check).
            var customerExists = await dbContext.Customers.AsNoTracking().AnyAsync(c => c.Id == request.CustomerId, ct);
            if (!customerExists)
            {
                return ValidationProblem("مشتری یافت نشد.");
            }
        }

        var lineExists = await dbContext.InsuranceLines.AsNoTracking().AnyAsync(l => l.Id == request.InsuranceLineId, ct);
        if (!lineExists)
        {
            return ValidationProblem("رشتهٔ بیمه یافت نشد.");
        }

        if (request.MarketerId is { } marketerId)
        {
            var marketerExists = await dbContext.Marketers.AsNoTracking().AnyAsync(m => m.Id == marketerId, ct);
            if (!marketerExists)
            {
                return ValidationProblem("بازاریاب یافت نشد.");
            }
        }

        var watch = new RenewalWatch
        {
            AgencyId = currentUser.ActiveOrganizationId,
            CustomerId = request.CustomerId,
            ProspectName = hasProspect ? request.ProspectName!.Trim() : null,
            ProspectMobile = hasProspect ? request.ProspectMobile!.Trim() : null,
            InsuranceLineId = request.InsuranceLineId,
            CurrentInsurer = request.CurrentInsurer?.Trim(),
            CurrentExpiryDate = request.CurrentExpiryDate,
            NotifyDaysBefore = request.NotifyDaysBefore,
            MarketerId = request.MarketerId,
            Status = RenewalWatchStatus.Watching,
        };
        dbContext.RenewalWatches.Add(watch);
        await dbContext.SaveChangesAsync(ct);

        var saved = await dbContext.RenewalWatches.AsNoTracking()
            .Include(w => w.Customer).Include(w => w.InsuranceLine).Include(w => w.Marketer)
            .FirstAsync(w => w.Id == watch.Id, ct);

        return Ok(ToDto(saved));
    }

    [HttpPost("{id:guid}/convert")]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<ActionResult<RenewalWatchDto>> Convert(Guid id, ConvertRenewalWatchRequest request, CancellationToken ct)
    {
        var watch = await dbContext.RenewalWatches.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (watch is null)
        {
            return NotFound();
        }

        if (watch.Status is RenewalWatchStatus.Converted or RenewalWatchStatus.Lost)
        {
            return ValidationProblem("این سررسید قبلاً بسته شده است.");
        }

        var policyExists = await dbContext.Policies.AsNoTracking().AnyAsync(p => p.Id == request.PolicyId, ct);
        if (!policyExists)
        {
            return ValidationProblem("بیمه‌نامه یافت نشد.");
        }

        watch.Status = RenewalWatchStatus.Converted;
        watch.PolicyId = request.PolicyId;
        await dbContext.SaveChangesAsync(ct);

        var saved = await dbContext.RenewalWatches.AsNoTracking()
            .Include(w => w.Customer).Include(w => w.InsuranceLine).Include(w => w.Marketer)
            .FirstAsync(w => w.Id == watch.Id, ct);

        return Ok(ToDto(saved));
    }

    [HttpPost("{id:guid}/lost")]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<ActionResult<RenewalWatchDto>> MarkLost(Guid id, CancellationToken ct)
    {
        var watch = await dbContext.RenewalWatches.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (watch is null)
        {
            return NotFound();
        }

        if (watch.Status is RenewalWatchStatus.Converted or RenewalWatchStatus.Lost)
        {
            return ValidationProblem("این سررسید قبلاً بسته شده است.");
        }

        watch.Status = RenewalWatchStatus.Lost;
        await dbContext.SaveChangesAsync(ct);

        var saved = await dbContext.RenewalWatches.AsNoTracking()
            .Include(w => w.Customer).Include(w => w.InsuranceLine).Include(w => w.Marketer)
            .FirstAsync(w => w.Id == watch.Id, ct);

        return Ok(ToDto(saved));
    }

    private static RenewalWatchDto ToDto(RenewalWatch w) => new(
        w.Id, w.CustomerId, w.Customer?.FullName, w.ProspectName, w.ProspectMobile,
        w.InsuranceLineId, w.InsuranceLine.NameFa, w.CurrentInsurer, w.CurrentExpiryDate, w.NotifyDaysBefore,
        w.MarketerId, w.Marketer?.FullName, w.Status.ToString(), w.PolicyId);

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
