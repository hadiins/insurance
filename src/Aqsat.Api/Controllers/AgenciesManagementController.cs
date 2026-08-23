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
/// Onboarding a new agency — Platform.Owner only. Creating just the Organization row would leave it
/// permanently unreachable: "کاربران و دسترسی‌ها" only ever lets someone already IN an agency add
/// more people to it, so the very first membership has to be created in the same request as the
/// agency itself.
/// </summary>
[ApiController]
[Route("api/platform/agencies")]
[Authorize(Policy = Permissions.PlatformOwner)]
public sealed class AgenciesManagementController(AppDbContext dbContext, IPasswordHasher passwordHasher) : ControllerBase
{
    /// <summary>No custom role has to exist yet — a fresh install has none besides the system owner
    /// role, so the first agency would otherwise be a dead end. Auto-created once, then reusable
    /// (and editable) like any other role from «مدیریت نقش‌ها».</summary>
    private const string DefaultManagerRoleName = "مدیر نمایندگی";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgencyDto>>> List(CancellationToken ct)
    {
        var agencies = await dbContext.Organizations.AsNoTracking()
            .Where(o => o.Level == OrganizationLevel.Agency && !o.IsDeleted)
            .OrderBy(o => o.Name)
            .ToListAsync(ct);

        var userCounts = await dbContext.UserOrgRoles.AsNoTracking()
            .Where(m => !m.IsDeleted)
            .GroupBy(m => m.OrganizationId)
            .Select(g => new { OrganizationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.OrganizationId, g => g.Count, ct);

        var result = agencies.Select(a => new AgencyDto(
            a.Id, a.Code, a.Name, a.City, a.InsurerName, a.IsActive, userCounts.GetValueOrDefault(a.Id))).ToList();

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CreateAgencyResultDto>> Create(CreateAgencyRequest request, CancellationToken ct)
    {
        var validationError = Validate(request);
        if (validationError is not null)
        {
            return ValidationProblem(validationError);
        }

        var codeTaken = await dbContext.Organizations.AnyAsync(o => o.Code == request.Code.Trim() && !o.IsDeleted, ct);
        if (codeTaken)
        {
            return ValidationProblem("این کد نمایندگی قبلاً استفاده شده است.");
        }

        var mobileTaken = await dbContext.Users.AnyAsync(u => u.Mobile == request.ManagerMobile.Trim() && !u.IsDeleted, ct);
        if (mobileTaken)
        {
            return ValidationProblem("این شمارهٔ همراه قبلاً برای کاربر دیگری ثبت شده است.");
        }

        Role role;
        if (request.RoleId is { } roleId)
        {
            var chosenRole = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId && !r.IsDeleted, ct);
            if (chosenRole is null || chosenRole.IsSystemRole)
            {
                return ValidationProblem("نقش یافت نشد.");
            }
            role = chosenRole;
        }
        else
        {
            role = await GetOrCreateDefaultManagerRoleAsync(ct);
        }

        var hq = await dbContext.Organizations.FirstOrDefaultAsync(o => o.Level == OrganizationLevel.Headquarters, ct);
        if (hq is null)
        {
            return ValidationProblem("دفتر مرکزی هنوز راه‌اندازی نشده است.");
        }

        var agency = new Organization
        {
            Level = OrganizationLevel.Agency,
            ParentId = hq.Id,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim(),
            InsurerName = string.IsNullOrWhiteSpace(request.InsurerName) ? null : request.InsurerName.Trim(),
            IsActive = true,
        };
        dbContext.Organizations.Add(agency);
        await dbContext.SaveChangesAsync(ct);

        var manager = new AppUser
        {
            FullName = request.ManagerFullName.Trim(),
            Mobile = request.ManagerMobile.Trim(),
            PasswordHash = passwordHasher.Hash(request.ManagerPassword),
            IsActive = true,
        };
        dbContext.Users.Add(manager);
        await dbContext.SaveChangesAsync(ct);

        dbContext.UserOrgRoles.Add(new UserOrgRole { UserId = manager.Id, OrganizationId = agency.Id, RoleId = role.Id });
        await dbContext.SaveChangesAsync(ct);

        var dto = new AgencyDto(agency.Id, agency.Code, agency.Name, agency.City, agency.InsurerName, agency.IsActive, 1);
        return Ok(new CreateAgencyResultDto(dto, manager.Mobile, role.Name));
    }

    private async Task<Role> GetOrCreateDefaultManagerRoleAsync(CancellationToken ct)
    {
        var existing = await dbContext.Roles.FirstOrDefaultAsync(
            r => r.Name == DefaultManagerRoleName && !r.IsDeleted && !r.IsSystemRole, ct);
        if (existing is not null)
        {
            return existing;
        }

        var role = new Role { Name = DefaultManagerRoleName, IsSystemRole = false };
        dbContext.Roles.Add(role);
        await dbContext.SaveChangesAsync(ct);

        foreach (var permission in Permissions.All.Where(p => p != Permissions.PlatformOwner))
        {
            dbContext.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
        }
        await dbContext.SaveChangesAsync(ct);

        return role;
    }

    private static string? Validate(CreateAgencyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return "کد و نام نمایندگی الزامی است.";
        }

        if (string.IsNullOrWhiteSpace(request.ManagerFullName) || string.IsNullOrWhiteSpace(request.ManagerMobile)
            || string.IsNullOrWhiteSpace(request.ManagerPassword))
        {
            return "نام، شمارهٔ همراه و رمز عبور اولین کاربر نمایندگی الزامی است.";
        }

        return null;
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
