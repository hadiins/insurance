using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// «کارمزد از بیمه‌گر» — the agency's own commission rate from the insurer, per insurance line
/// (including sub-lines). Mirrors MarketersController's rate endpoints, minus the MarketerId
/// dimension. Locked at issuance into Policy.AgencyCommissionPercent, same as MarketerRatePercent.
/// </summary>
[ApiController]
[Route("api/settings/agency-commission-rates")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class AgencyCommissionRatesController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgencyCommissionRateDto>>> List(CancellationToken ct)
    {
        var rates = await dbContext.AgencyCommissionRates
            .AsNoTracking()
            .Include(r => r.InsuranceLine)
            .OrderByDescending(r => r.EffectiveFrom)
            .Select(r => new AgencyCommissionRateDto(r.Id, r.InsuranceLineId, r.InsuranceLine.NameFa, r.RatePercent, r.EffectiveFrom, r.EffectiveTo))
            .ToListAsync(ct);

        return Ok(rates);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult<AgencyCommissionRateDto>> SetRate(SetAgencyCommissionRateRequest request, CancellationToken ct)
    {
        if (request.RatePercent <= 0)
        {
            return ValidationProblem("درصد کارمزد باید مثبت باشد.");
        }

        var line = await dbContext.InsuranceLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == request.InsuranceLineId, ct);
        if (line is null)
        {
            return ValidationProblem("رشتهٔ بیمه یافت نشد.");
        }

        // Never update a prior rate row in place — close it, then insert the new one, so
        // Policy.AgencyCommissionPercent locked at past issuances stays meaningful.
        var previous = await dbContext.AgencyCommissionRates
            .Where(r => r.InsuranceLineId == request.InsuranceLineId && r.EffectiveTo == null)
            .FirstOrDefaultAsync(ct);
        if (previous is not null)
        {
            previous.EffectiveTo = request.EffectiveFrom.AddDays(-1);
        }

        var rate = new AgencyCommissionRate
        {
            AgencyId = currentUser.ActiveOrganizationId,
            InsuranceLineId = request.InsuranceLineId,
            RatePercent = request.RatePercent,
            EffectiveFrom = request.EffectiveFrom,
        };
        dbContext.AgencyCommissionRates.Add(rate);
        await dbContext.SaveChangesAsync(ct);

        return Ok(new AgencyCommissionRateDto(rate.Id, rate.InsuranceLineId, line.NameFa, rate.RatePercent, rate.EffectiveFrom, rate.EffectiveTo));
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
