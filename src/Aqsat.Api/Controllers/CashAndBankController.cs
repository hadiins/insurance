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
/// «صندوق و بانک‌ها» — agency-defined cash boxes and bank accounts, shown as a dropdown on every
/// cash-receipt form (down payment, installment payment, non-installment full payment).
/// </summary>
[ApiController]
[Route("api/settings/cash-and-bank")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class CashAndBankController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet("cash-boxes")]
    public async Task<ActionResult<IReadOnlyList<CashBoxDto>>> CashBoxes(CancellationToken ct)
    {
        var boxes = await dbContext.CashBoxes.AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new CashBoxDto(b.Id, b.Name, b.IsActive))
            .ToListAsync(ct);
        return Ok(boxes);
    }

    [HttpPost("cash-boxes")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult<CashBoxDto>> CreateCashBox(CreateCashBoxRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ValidationProblem("نام صندوق الزامی است.");
        }

        var entity = new CashBox { AgencyId = currentUser.ActiveOrganizationId, Name = request.Name.Trim(), IsActive = true };
        dbContext.CashBoxes.Add(entity);
        await dbContext.SaveChangesAsync(ct);
        return Ok(new CashBoxDto(entity.Id, entity.Name, entity.IsActive));
    }

    [HttpPut("cash-boxes/{id:guid}")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult<CashBoxDto>> UpdateCashBox(Guid id, UpdateCashBoxRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ValidationProblem("نام صندوق الزامی است.");
        }

        var entity = await dbContext.CashBoxes.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (entity is null)
        {
            return NotFound();
        }

        entity.Name = request.Name.Trim();
        entity.IsActive = request.IsActive;
        await dbContext.SaveChangesAsync(ct);
        return Ok(new CashBoxDto(entity.Id, entity.Name, entity.IsActive));
    }

    [HttpDelete("cash-boxes/{id:guid}")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult> DeleteCashBox(Guid id, CancellationToken ct)
    {
        var entity = await dbContext.CashBoxes.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (entity is null)
        {
            return NotFound();
        }

        entity.IsDeleted = true;
        entity.DeletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("bank-accounts")]
    public async Task<ActionResult<IReadOnlyList<BankAccountDto>>> BankAccounts(CancellationToken ct)
    {
        var accounts = await dbContext.BankAccounts.AsNoTracking()
            .OrderBy(a => a.BankName)
            .Select(a => new BankAccountDto(a.Id, a.BankName, a.AccountNumber, a.AccountHolderName, a.IsActive))
            .ToListAsync(ct);
        return Ok(accounts);
    }

    [HttpPost("bank-accounts")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult<BankAccountDto>> CreateBankAccount(CreateBankAccountRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BankName) || string.IsNullOrWhiteSpace(request.AccountNumber))
        {
            return ValidationProblem("نام بانک و شمارهٔ حساب الزامی است.");
        }

        var entity = new BankAccount
        {
            AgencyId = currentUser.ActiveOrganizationId,
            BankName = request.BankName.Trim(),
            AccountNumber = request.AccountNumber.Trim(),
            AccountHolderName = string.IsNullOrWhiteSpace(request.AccountHolderName) ? null : request.AccountHolderName.Trim(),
            IsActive = true,
        };
        dbContext.BankAccounts.Add(entity);
        await dbContext.SaveChangesAsync(ct);
        return Ok(new BankAccountDto(entity.Id, entity.BankName, entity.AccountNumber, entity.AccountHolderName, entity.IsActive));
    }

    [HttpPut("bank-accounts/{id:guid}")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult<BankAccountDto>> UpdateBankAccount(Guid id, UpdateBankAccountRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BankName) || string.IsNullOrWhiteSpace(request.AccountNumber))
        {
            return ValidationProblem("نام بانک و شمارهٔ حساب الزامی است.");
        }

        var entity = await dbContext.BankAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (entity is null)
        {
            return NotFound();
        }

        entity.BankName = request.BankName.Trim();
        entity.AccountNumber = request.AccountNumber.Trim();
        entity.AccountHolderName = string.IsNullOrWhiteSpace(request.AccountHolderName) ? null : request.AccountHolderName.Trim();
        entity.IsActive = request.IsActive;
        await dbContext.SaveChangesAsync(ct);
        return Ok(new BankAccountDto(entity.Id, entity.BankName, entity.AccountNumber, entity.AccountHolderName, entity.IsActive));
    }

    [HttpDelete("bank-accounts/{id:guid}")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult> DeleteBankAccount(Guid id, CancellationToken ct)
    {
        var entity = await dbContext.BankAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
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
