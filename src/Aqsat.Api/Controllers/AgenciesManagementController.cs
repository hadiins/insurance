using System.Linq.Expressions;
using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Stats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// Owner-side agency management (پروندهٔ نمایندگی) — Platform.Owner only. The list, filter options
/// and summary read only RLS-free tables (Organizations + the AgencyStatsDaily rollup) so they stay
/// fast and correct across tens of thousands of tenants; the per-agency profile switches into that
/// agency's RLS scope via AgencyStatsService for live, exact numbers.
/// </summary>
[ApiController]
[Route("api/platform/agencies")]
[Authorize(Policy = Permissions.PlatformOwner)]
public sealed class AgenciesManagementController(
    AppDbContext dbContext, IPasswordHasher passwordHasher, AgencyStatsService statsService) : ControllerBase
{
    /// <summary>No custom role has to exist yet — a fresh install has none besides the system owner
    /// role, so the first agency would otherwise be a dead end. Auto-created once, then reusable
    /// (and editable) like any other role from «مدیریت نقش‌ها».</summary>
    private const string DefaultManagerRoleName = "مدیر نمایندگی";

    private const int TrendMonths = 12;

    [HttpGet]
    public async Task<ActionResult<AgencyListPageDto>> List(
        [FromQuery] string? province, [FromQuery] string? city, [FromQuery] string? insurer,
        [FromQuery] bool? isActive, [FromQuery] string? search,
        [FromQuery] string? sortBy, [FromQuery] string? sortDir,
        [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize is <= 0 or > 200 ? 50 : pageSize;

        var orgs = BuildFilteredQuery(province, city, insurer, isActive, search);
        var totalCount = await orgs.CountAsync(ct);

        // Sort BEFORE the projection — EF cannot translate OrderBy on a projected DTO member, and
        // the stat keys are correlated subqueries SQL Server is perfectly happy to ORDER BY.
        var ordered = SortOrganizations(orgs, sortBy, sortDir);

        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new AgencyListRowDto(
                o.Id,
                o.Code,
                o.Name,
                o.Province,
                o.City,
                o.InsurerName,
                o.IsActive,
                dbContext.UserOrgRoles.Count(m => m.OrganizationId == o.Id),
                dbContext.AgencyStatsDaily.Where(s => s.AgencyId == o.Id).Sum(s => (int?)s.PoliciesIssued) ?? 0,
                dbContext.AgencyStatsDaily.Where(s => s.AgencyId == o.Id).Sum(s => (int?)s.SmsSentCount) ?? 0,
                dbContext.AgencyStatsDaily.Where(s => s.AgencyId == o.Id).Sum(s => (int?)s.InquiryPaymentsCount) ?? 0,
                dbContext.AgencyStatsDaily.Where(s => s.AgencyId == o.Id).Sum(s => (int?)s.InquiryCallsCount) ?? 0,
                dbContext.AgencyStatsDaily.Where(s => s.AgencyId == o.Id).Sum(s => (decimal?)s.InquiryRevenueToman) ?? 0))
            .ToListAsync(ct);

        return Ok(new AgencyListPageDto(totalCount, rows));
    }

    [HttpGet("filters")]
    public async Task<ActionResult<AgencyFilterOptionsDto>> FilterOptions(CancellationToken ct)
    {
        var agencyOrgs = dbContext.Organizations.AsNoTracking()
            .Where(o => o.Level == OrganizationLevel.Agency && !o.IsDeleted);

        var cities = await agencyOrgs
            .Where(o => o.City != null)
            .Select(o => o.City!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

        var insurers = await agencyOrgs
            .Where(o => o.InsurerName != null)
            .Select(o => o.InsurerName!)
            .Distinct()
            .OrderBy(i => i)
            .ToListAsync(ct);

        return Ok(new AgencyFilterOptionsDto(IranProvinces.All, cities, insurers));
    }

    [HttpGet("summary")]
    public async Task<ActionResult<AgencyPlatformSummaryDto>> Summary(CancellationToken ct)
    {
        var orgs = await dbContext.Organizations.AsNoTracking()
            .Where(o => o.Level == OrganizationLevel.Agency && !o.IsDeleted)
            .Select(o => new { o.Id, o.Province, o.IsActive })
            .ToListAsync(ct);

        var stats = await dbContext.AgencyStatsDaily.AsNoTracking()
            .GroupBy(s => s.AgencyId)
            .Select(g => new
            {
                AgencyId = g.Key,
                Policies = g.Sum(x => x.PoliciesIssued),
                Sms = g.Sum(x => x.SmsSentCount),
                SmsCost = g.Sum(x => x.SmsCostToman),
                InquiryPayments = g.Sum(x => x.InquiryPaymentsCount),
                InquiryRevenue = g.Sum(x => x.InquiryRevenueToman),
                InquiryCalls = g.Sum(x => x.InquiryCallsCount),
            })
            .ToDictionaryAsync(s => s.AgencyId, ct);

        var byProvince = orgs
            .GroupBy(o => o.Province)
            .Select(g => new AgencyProvinceStatDto(
                g.Key,
                g.Count(),
                g.Sum(o => stats.TryGetValue(o.Id, out var s) ? s.Policies : 0),
                g.Sum(o => stats.TryGetValue(o.Id, out var s) ? s.Sms : 0),
                g.Sum(o => stats.TryGetValue(o.Id, out var s) ? s.InquiryPayments : 0),
                g.Sum(o => stats.TryGetValue(o.Id, out var s) ? s.InquiryRevenue : 0m)))
            .ToList();

        var dto = new AgencyPlatformSummaryDto(
            orgs.Count,
            orgs.Count(o => o.IsActive),
            orgs.Sum(o => stats.TryGetValue(o.Id, out var s) ? s.Policies : 0),
            orgs.Sum(o => stats.TryGetValue(o.Id, out var s) ? s.Sms : 0),
            orgs.Sum(o => stats.TryGetValue(o.Id, out var s) ? s.SmsCost : 0m),
            orgs.Sum(o => stats.TryGetValue(o.Id, out var s) ? s.InquiryPayments : 0),
            orgs.Sum(o => stats.TryGetValue(o.Id, out var s) ? s.InquiryRevenue : 0m),
            orgs.Sum(o => stats.TryGetValue(o.Id, out var s) ? s.InquiryCalls : 0),
            byProvince.OrderByDescending(p => p.AgencyCount).ToList());

        return Ok(dto);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AgencyProfileDto>> GetProfile(Guid id, CancellationToken ct)
    {
        var agency = await dbContext.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.Level == OrganizationLevel.Agency && !o.IsDeleted, ct);
        if (agency is null)
        {
            return NotFoundProblem("نمایندگی یافت نشد.");
        }

        var userCount = await dbContext.UserOrgRoles.AsNoTracking()
            .CountAsync(m => m.OrganizationId == id, ct);

        var stats = await statsService.GetLiveStatsAsync(id, TrendMonths, ct);

        return Ok(new AgencyProfileDto(
            agency.Id, agency.Code, agency.Name, agency.Province, agency.City, agency.InsurerName,
            agency.IsActive, agency.AgencyCode, userCount,
            stats.PoliciesIssuedTotal, stats.PoliciesThisMonth,
            stats.SmsSentTotal, stats.SmsCostToman,
            stats.InquiryPaymentsTotal, stats.InquiryRevenueToman,
            stats.InquiryCallsTotal, stats.InquiryCallCostToman,
            stats.MonthlyTrend.Select(m => new AgencyMonthlyTrendDto(
                m.Year, m.Month, m.PoliciesIssued, m.SmsSent, m.InquiryPayments, m.InquiryCalls, m.InquiryRevenueToman)).ToList()));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AgencyDto>> Update(Guid id, UpdateAgencyRequest request, CancellationToken ct)
    {
        var agency = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == id && o.Level == OrganizationLevel.Agency && !o.IsDeleted, ct);
        if (agency is null)
        {
            return NotFoundProblem("نمایندگی یافت نشد.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ValidationProblem("نام نمایندگی الزامی است.");
        }

        if (request.Province is { } province && !IranProvinces.All.Contains(province))
        {
            return ValidationProblem("استان انتخاب‌شده معتبر نیست.");
        }

        agency.Name = request.Name.Trim();
        agency.Province = NormalizeOptional(request.Province);
        agency.City = NormalizeOptional(request.City);
        agency.InsurerName = NormalizeOptional(request.InsurerName);
        agency.IsActive = request.IsActive;
        await dbContext.SaveChangesAsync(ct);

        var userCount = await dbContext.UserOrgRoles.AsNoTracking()
            .CountAsync(m => m.OrganizationId == id, ct);

        return Ok(new AgencyDto(agency.Id, agency.Code, agency.Name, agency.Province, agency.City,
            agency.InsurerName, agency.IsActive, userCount));
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

        if (request.Province is { } province && !IranProvinces.All.Contains(province))
        {
            return ValidationProblem("استان انتخاب‌شده معتبر نیست.");
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
            Province = NormalizeOptional(request.Province),
            City = NormalizeOptional(request.City),
            InsurerName = NormalizeOptional(request.InsurerName),
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

        var dto = new AgencyDto(agency.Id, agency.Code, agency.Name, agency.Province, agency.City, agency.InsurerName, agency.IsActive, 1);
        return Ok(new CreateAgencyResultDto(dto, manager.Mobile, role.Name));
    }

    private IQueryable<Organization> BuildFilteredQuery(
        string? province, string? city, string? insurer, bool? isActive, string? search)
    {
        var query = dbContext.Organizations.AsNoTracking()
            .Where(o => o.Level == OrganizationLevel.Agency && !o.IsDeleted);

        if (!string.IsNullOrWhiteSpace(province))
        {
            query = query.Where(o => o.Province == province);
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            query = query.Where(o => o.City == city);
        }

        if (!string.IsNullOrWhiteSpace(insurer))
        {
            query = query.Where(o => o.InsurerName == insurer);
        }

        if (isActive is { } active)
        {
            query = query.Where(o => o.IsActive == active);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Persian digits normalize to Latin so «۵۷۶۲۱۰» finds the same rows as "576210".
            var term = DigitNormalizer.ToLatin(search.Trim());
            query = query.Where(o => o.Name.Contains(term) || o.Code.Contains(term));
        }

        return query;
    }

    /// <summary>Sort keys mirror the list columns. Every stat key is the same correlated scalar
    /// subquery the projection uses, so SQL Server does the ordering — essential at 60k rows.</summary>
    private IQueryable<Organization> SortOrganizations(
        IQueryable<Organization> query, string? sortBy, string? sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        return (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "policies" => ApplySort(query, o => dbContext.AgencyStatsDaily.Where(s => s.AgencyId == o.Id).Sum(s => (int?)s.PoliciesIssued) ?? 0, descending),
            "sms" => ApplySort(query, o => dbContext.AgencyStatsDaily.Where(s => s.AgencyId == o.Id).Sum(s => (int?)s.SmsSentCount) ?? 0, descending),
            "inquiries" => ApplySort(query, o => dbContext.AgencyStatsDaily.Where(s => s.AgencyId == o.Id).Sum(s => (int?)s.InquiryPaymentsCount) ?? 0, descending),
            "inquirycalls" => ApplySort(query, o => dbContext.AgencyStatsDaily.Where(s => s.AgencyId == o.Id).Sum(s => (int?)s.InquiryCallsCount) ?? 0, descending),
            "revenue" => ApplySort(query, o => dbContext.AgencyStatsDaily.Where(s => s.AgencyId == o.Id).Sum(s => (decimal?)s.InquiryRevenueToman) ?? 0m, descending),
            "users" => ApplySort(query, o => dbContext.UserOrgRoles.Count(m => m.OrganizationId == o.Id), descending),
            "code" => ApplySort(query, o => o.Code, descending),
            _ => ApplySort(query, o => o.Name, descending),
        };
    }

    private static IQueryable<Organization> ApplySort<T>(
        IQueryable<Organization> query, Expression<Func<Organization, T>> key, bool descending) =>
        descending ? query.OrderByDescending(key) : query.OrderBy(key);

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

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

        // Same 8-character minimum every other account-creation path enforces — an agency manager
        // account with a one-character password is the weakest link in the whole tenant.
        if (request.ManagerPassword.Length < 8)
        {
            return "رمز عبور باید حداقل ۸ کاراکتر باشد.";
        }

        return null;
    }

    private ActionResult NotFoundProblem(string message) => NotFound(new ProblemDetails
    {
        Status = StatusCodes.Status404NotFound,
        Title = message,
    });

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
