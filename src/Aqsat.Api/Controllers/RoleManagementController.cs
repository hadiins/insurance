using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// مدیریت نقش‌ها — Role is deliberately shared across every agency, not per-agency
/// (docs/PHASE-1-SPEC.md §2.2: "role is separate from organization"), so defining what a role's
/// permission set actually IS belongs to the platform owner, not any one agency's manager. An
/// agency's own "کاربران و دسترسی‌ها" page only ever ASSIGNS one of these roles to a person — it
/// never edits the role itself.
/// </summary>
[ApiController]
[Route("api/platform/roles")]
[Authorize(Policy = Permissions.PlatformOwner)]
public sealed class RoleManagementController(AppDbContext dbContext) : ControllerBase
{
    private static readonly (string Key, string Label)[] AssignablePermissionCatalog =
    [
        (Permissions.PolicyRead, "مشاهدهٔ بیمه‌نامه‌ها"),
        (Permissions.PolicyWrite, "ثبت و ویرایش بیمه‌نامه‌ها"),
        (Permissions.PaymentWrite, "ثبت پرداخت"),
        (Permissions.ImportRun, "ورود اطلاعات (Import)"),
        (Permissions.SettingsWrite, "ویرایش تنظیمات نمایندگی"),
        (Permissions.LockForceRelease, "آزادسازی اجباری قفل ویرایش"),
        (Permissions.ReportRead, "مشاهدهٔ گزارش‌ها"),
        (Permissions.FinanceRead, "مشاهدهٔ سود و زیان"),
        (Permissions.MarketerManage, "مدیریت بازاریاب‌ها و کارمزد"),
        (Permissions.MarketerSelfView, "پنل بازاریاب (مشاهدهٔ خودش)"),
        // Permissions.PlatformOwner is deliberately absent — never assignable through a role an
        // agency could hand to its own staff (docs/UPDATE-SYSTEM.md rule 1).
    ];

    [HttpGet("permissions-catalog")]
    public ActionResult<IReadOnlyList<PermissionCatalogItemDto>> PermissionsCatalog() =>
        Ok(AssignablePermissionCatalog.Select(p => new PermissionCatalogItemDto(p.Key, p.Label)).ToList());

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken ct)
    {
        var roles = await dbContext.Roles.AsNoTracking()
            .Where(r => !r.IsDeleted)
            .Include(r => r.RolePermissions)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        var memberCounts = await dbContext.UserOrgRoles.AsNoTracking()
            .Where(m => !m.IsDeleted)
            .GroupBy(m => m.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.RoleId, g => g.Count, ct);

        var result = roles.Select(r => new RoleDto(
            r.Id, r.Name, r.RolePermissions.Where(p => !p.IsDeleted).Select(p => p.Permission).ToList(),
            r.IsSystemRole, memberCounts.GetValueOrDefault(r.Id))).ToList();

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<RoleDto>> Create(SaveRoleRequest request, CancellationToken ct)
    {
        var validationError = Validate(request);
        if (validationError is not null)
        {
            return ValidationProblem(validationError);
        }

        var nameTaken = await dbContext.Roles.AnyAsync(r => r.Name == request.Name.Trim() && !r.IsDeleted, ct);
        if (nameTaken)
        {
            return ValidationProblem("نقشی با این نام از قبل وجود دارد.");
        }

        var role = new Role { Name = request.Name.Trim(), IsSystemRole = false };
        dbContext.Roles.Add(role);
        // role.Id is server-generated (NEWSEQUENTIALID()) — must round-trip through SaveChanges
        // before it's usable as a foreign key on the RolePermission rows below.
        await dbContext.SaveChangesAsync(ct);

        foreach (var permission in request.Permissions.Distinct())
        {
            dbContext.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
        }
        await dbContext.SaveChangesAsync(ct);

        return Ok(new RoleDto(role.Id, role.Name, request.Permissions.Distinct().ToList(), false, 0));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RoleDto>> Update(Guid id, SaveRoleRequest request, CancellationToken ct)
    {
        var validationError = Validate(request);
        if (validationError is not null)
        {
            return ValidationProblem(validationError);
        }

        var role = await dbContext.Roles.Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct);
        if (role is null)
        {
            return NotFound();
        }

        if (role.IsSystemRole)
        {
            return ValidationProblem("نقش سامانه‌ای مالک از اینجا قابل ویرایش نیست.");
        }

        var nameTaken = await dbContext.Roles.AnyAsync(r => r.Id != id && r.Name == request.Name.Trim() && !r.IsDeleted, ct);
        if (nameTaken)
        {
            return ValidationProblem("نقشی با این نام از قبل وجود دارد.");
        }

        role.Name = request.Name.Trim();

        var desired = request.Permissions.Distinct().ToHashSet();
        foreach (var existing in role.RolePermissions.Where(p => !p.IsDeleted))
        {
            if (!desired.Contains(existing.Permission))
            {
                existing.IsDeleted = true;
                existing.DeletedAt = DateTimeOffset.UtcNow;
            }
        }
        var current = role.RolePermissions.Where(p => !p.IsDeleted).Select(p => p.Permission).ToHashSet();
        foreach (var toAdd in desired.Except(current))
        {
            dbContext.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = toAdd });
        }

        await dbContext.SaveChangesAsync(ct);

        return Ok(new RoleDto(role.Id, role.Name, desired.ToList(), false,
            await dbContext.UserOrgRoles.CountAsync(m => m.RoleId == role.Id && !m.IsDeleted, ct)));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var role = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct);
        if (role is null)
        {
            return NotFound();
        }

        if (role.IsSystemRole)
        {
            return ValidationProblem("نقش سامانه‌ای مالک قابل حذف نیست.");
        }

        var inUse = await dbContext.UserOrgRoles.AnyAsync(m => m.RoleId == id && !m.IsDeleted, ct);
        if (inUse)
        {
            return ValidationProblem("این نقش به کاربرانی اختصاص دارد و قابل حذف نیست. ابتدا دسترسی آن‌ها را تغییر دهید.");
        }

        role.IsDeleted = true;
        role.DeletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    private static string? Validate(SaveRoleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "نام نقش الزامی است.";
        }

        var allowed = AssignablePermissionCatalog.Select(p => p.Key).ToHashSet();
        if (request.Permissions.Any(p => !allowed.Contains(p)))
        {
            return "یکی از مجوزهای انتخاب‌شده معتبر نیست.";
        }

        return null;
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
