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
/// The 80% problem (docs/PHASE-1-SPEC.md): agencies configure which contract names mean
/// "installment" here — ContractTemplateMatcher (Task 7) reads these rows at import time.
/// </summary>
[ApiController]
[Route("api/contract-templates")]
[Authorize(Policy = Permissions.PolicyWrite)]
public sealed class ContractTemplatesController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContractTemplateDto>>> List(CancellationToken ct)
    {
        var templates = await dbContext.ContractTemplates
            .AsNoTracking()
            .OrderBy(t => t.ContractNamePattern)
            .Select(t => new ContractTemplateDto(t.Id, t.ContractNamePattern, t.IsInstallment, t.DefaultInstallmentCount, t.SuggestedDownPaymentPercent))
            .ToListAsync(ct);

        return Ok(templates);
    }

    [HttpPost]
    public async Task<ActionResult<ContractTemplateDto>> Create(SaveContractTemplateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ContractNamePattern))
        {
            return ValidationProblem("الگوی نام قرارداد الزامی است.");
        }

        var template = new ContractTemplate
        {
            AgencyId = currentUser.ActiveOrganizationId,
            ContractNamePattern = request.ContractNamePattern.Trim(),
            IsInstallment = request.IsInstallment,
            DefaultInstallmentCount = request.DefaultInstallmentCount,
            SuggestedDownPaymentPercent = request.SuggestedDownPaymentPercent,
        };
        dbContext.ContractTemplates.Add(template);
        await dbContext.SaveChangesAsync(ct);

        return Ok(new ContractTemplateDto(
            template.Id, template.ContractNamePattern, template.IsInstallment,
            template.DefaultInstallmentCount, template.SuggestedDownPaymentPercent));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ContractTemplateDto>> Update(Guid id, SaveContractTemplateRequest request, CancellationToken ct)
    {
        var template = await dbContext.ContractTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.ContractNamePattern))
        {
            return ValidationProblem("الگوی نام قرارداد الزامی است.");
        }

        template.ContractNamePattern = request.ContractNamePattern.Trim();
        template.IsInstallment = request.IsInstallment;
        template.DefaultInstallmentCount = request.DefaultInstallmentCount;
        template.SuggestedDownPaymentPercent = request.SuggestedDownPaymentPercent;
        await dbContext.SaveChangesAsync(ct);

        return Ok(new ContractTemplateDto(
            template.Id, template.ContractNamePattern, template.IsInstallment,
            template.DefaultInstallmentCount, template.SuggestedDownPaymentPercent));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var template = await dbContext.ContractTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return NotFound();
        }

        // No hard deletes anywhere in this system (CLAUDE.md rule 7).
        template.IsDeleted = true;
        template.DeletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
