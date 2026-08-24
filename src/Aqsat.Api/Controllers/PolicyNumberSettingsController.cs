using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Infrastructure.Numbering;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASK-24-POLICY-NUMBER.md §7 — «تنظیمات ← کدهای بیمه‌نامه»: which numeric code (per the
/// currently-configured insurer) maps to which InsuranceLine, plus the tunable parts of the number
/// format itself. InsurerName/Pattern stay fixed to what PolicyNumberDefaultsSeeder seeds — only
/// LineCodeLength/AgencyCodeLength/YearDigits/SerialLength/Separator/IsStrict are agency-editable.
/// </summary>
[ApiController]
[Route("api/settings/policy-number")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class PolicyNumberSettingsController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet("line-codes")]
    public async Task<ActionResult<IReadOnlyList<InsuranceLineCodeDto>>> LineCodes(CancellationToken ct)
    {
        await EnsureDefaultsAsync(ct);

        var codes = await dbContext.InsuranceLineCodes.AsNoTracking()
            .Include(c => c.InsuranceLine)
            .OrderBy(c => c.Code)
            .Select(c => new InsuranceLineCodeDto(c.Id, c.InsuranceLineId, c.InsuranceLine.NameFa, c.Code, c.IsActive))
            .ToListAsync(ct);

        return Ok(codes);
    }

    [HttpPost("line-codes")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult<InsuranceLineCodeDto>> CreateLineCode(CreateInsuranceLineCodeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return ValidationProblem("کد الزامی است.");
        }

        var format = await EnsureDefaultsAsync(ct);
        var line = await dbContext.InsuranceLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == request.InsuranceLineId, ct);
        if (line is null)
        {
            return ValidationProblem("رشتهٔ بیمه یافت نشد.");
        }

        var code = request.Code.Trim();
        var duplicateCode = await dbContext.InsuranceLineCodes.AsNoTracking()
            .AnyAsync(c => c.InsurerName == format.InsurerName && c.Code == code, ct);
        if (duplicateCode)
        {
            return ValidationProblem($"کد «{code}» قبلاً برای رشتهٔ دیگری ثبت شده است.");
        }

        var duplicateLine = await dbContext.InsuranceLineCodes.AsNoTracking()
            .AnyAsync(c => c.InsurerName == format.InsurerName && c.InsuranceLineId == request.InsuranceLineId, ct);
        if (duplicateLine)
        {
            return ValidationProblem($"رشتهٔ «{line.NameFa}» قبلاً کد دیگری دارد.");
        }

        var entity = new InsuranceLineCode
        {
            AgencyId = currentUser.ActiveOrganizationId,
            InsuranceLineId = request.InsuranceLineId,
            InsurerName = format.InsurerName,
            Code = code,
            IsActive = true,
        };
        dbContext.InsuranceLineCodes.Add(entity);
        await dbContext.SaveChangesAsync(ct);

        return Ok(new InsuranceLineCodeDto(entity.Id, entity.InsuranceLineId, line.NameFa, entity.Code, entity.IsActive));
    }

    [HttpPut("line-codes/{id:guid}")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult<InsuranceLineCodeDto>> UpdateLineCode(Guid id, UpdateInsuranceLineCodeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return ValidationProblem("کد الزامی است.");
        }

        var entity = await dbContext.InsuranceLineCodes.Include(c => c.InsuranceLine).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return NotFound();
        }

        var code = request.Code.Trim();
        var duplicateCode = await dbContext.InsuranceLineCodes.AsNoTracking()
            .AnyAsync(c => c.Id != id && c.InsurerName == entity.InsurerName && c.Code == code, ct);
        if (duplicateCode)
        {
            return ValidationProblem($"کد «{code}» قبلاً برای رشتهٔ دیگری ثبت شده است.");
        }

        entity.Code = code;
        entity.IsActive = request.IsActive;
        await dbContext.SaveChangesAsync(ct);

        return Ok(new InsuranceLineCodeDto(entity.Id, entity.InsuranceLineId, entity.InsuranceLine.NameFa, entity.Code, entity.IsActive));
    }

    [HttpDelete("line-codes/{id:guid}")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult> DeleteLineCode(Guid id, CancellationToken ct)
    {
        var entity = await dbContext.InsuranceLineCodes.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return NotFound();
        }

        entity.IsDeleted = true;
        entity.DeletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("format")]
    public async Task<ActionResult<PolicyNumberFormatDto>> GetFormat(CancellationToken ct)
    {
        var format = await EnsureDefaultsAsync(ct);
        return Ok(ToDto(format));
    }

    [HttpPut("format")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult<PolicyNumberFormatDto>> UpdateFormat(UpdatePolicyNumberFormatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Separator))
        {
            return ValidationProblem("جداکننده الزامی است.");
        }

        if (request.LineCodeLength <= 0 || request.AgencyCodeLength <= 0 || request.SerialLength <= 0
            || (request.YearDigits != 3 && request.YearDigits != 4))
        {
            return ValidationProblem("طول‌ها باید مثبت باشند و رقم سال باید ۳ یا ۴ باشد.");
        }

        await PolicyNumberDefaultsSeeder.EnsureAgencyDefaultsAsync(dbContext, currentUser.ActiveOrganizationId, ct);
        var format = await dbContext.PolicyNumberFormats.FirstAsync(f => f.IsActive, ct);

        format.Separator = request.Separator.Trim();
        format.LineCodeLength = request.LineCodeLength;
        format.AgencyCodeLength = request.AgencyCodeLength;
        format.YearDigits = request.YearDigits;
        format.SerialLength = request.SerialLength;
        format.IsStrict = request.IsStrict;
        await dbContext.SaveChangesAsync(ct);

        return Ok(ToDto(format));
    }

    private async Task<PolicyNumberFormat> EnsureDefaultsAsync(CancellationToken ct)
    {
        await PolicyNumberDefaultsSeeder.EnsureAgencyDefaultsAsync(dbContext, currentUser.ActiveOrganizationId, ct);
        return await dbContext.PolicyNumberFormats.FirstAsync(f => f.IsActive, ct);
    }

    private static PolicyNumberFormatDto ToDto(PolicyNumberFormat f) => new(
        f.Id, f.InsurerName, f.Pattern, f.Separator, f.LineCodeLength, f.AgencyCodeLength, f.YearDigits, f.SerialLength, f.IsStrict);

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
