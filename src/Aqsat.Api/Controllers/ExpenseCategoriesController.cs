using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>«دسته‌بندی هزینه‌ها» — agency-definable, never a fixed system list.</summary>
[ApiController]
[Route("api/settings/expense-categories")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class ExpenseCategoriesController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExpenseCategoryDto>>> List(CancellationToken ct)
    {
        var categories = await dbContext.ExpenseCategories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ExpenseCategoryDto(c.Id, c.Name, c.IsActive))
            .ToListAsync(ct);
        return Ok(categories);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult<ExpenseCategoryDto>> Create(CreateExpenseCategoryRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ValidationProblem("نام دسته الزامی است.");
        }

        var entity = new ExpenseCategory { AgencyId = currentUser.ActiveOrganizationId, Name = request.Name.Trim(), IsActive = true };
        dbContext.ExpenseCategories.Add(entity);
        await dbContext.SaveChangesAsync(ct);
        return Ok(new ExpenseCategoryDto(entity.Id, entity.Name, entity.IsActive));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult<ExpenseCategoryDto>> Update(Guid id, UpdateExpenseCategoryRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ValidationProblem("نام دسته الزامی است.");
        }

        var entity = await dbContext.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return NotFound();
        }

        entity.Name = request.Name.Trim();
        entity.IsActive = request.IsActive;
        await dbContext.SaveChangesAsync(ct);
        return Ok(new ExpenseCategoryDto(entity.Id, entity.Name, entity.IsActive));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entity = await dbContext.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return NotFound();
        }

        entity.IsDeleted = true;
        entity.DeletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
