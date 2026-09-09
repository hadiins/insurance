using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Application.Platform;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// Self-serve agency signup (feature 5) — an insurance agent creates their own agency and manager
/// account without the platform owner keying them in. Gated by the owner's PlatformSignupSettings
/// switch (absent row = closed, the safe default), the manager's mobile is verified with an SMS
/// OTP, and the organization is created PENDING (IsActive=false, owner decision 2026-09-08): the
/// manager cannot log in until the owner activates the agency from «مدیریت نمایندگی‌ها». No token
/// is ever returned here — the session starts at the first successful login after approval.
/// </summary>
[ApiController]
[Route("api/signup")]
[AllowAnonymous]
public sealed class SignupController(
    AppDbContext dbContext, IPasswordHasher passwordHasher, ISignupOtpService signupOtpService) : ControllerBase
{
    /// <summary>Same name and permission set as AgenciesManagementController's auto-provisioned
    /// role, so an owner-created agency and a self-registered one end up identical.</summary>
    private const string DefaultManagerRoleName = "مدیر نمایندگی";

    [HttpGet("status")]
    public async Task<ActionResult<SignupStatusDto>> Status(CancellationToken ct) =>
        Ok(new SignupStatusDto(await IsSignupOpenAsync(ct)));

    [HttpPost("request-otp")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult> RequestOtp(SignupRequestOtpRequest request, CancellationToken ct)
    {
        if (!await IsSignupOpenAsync(ct))
        {
            return SignupClosed();
        }

        var mobile = MobileNumberValidator.Normalize(request.Mobile);
        if (!MobileNumberValidator.IsValid(mobile))
        {
            return ValidationProblem("شمارهٔ همراه نامعتبر است.");
        }

        // Checked BEFORE the SMS goes out — an OTP sent to the owner of an already-registered
        // number would both cost money and let someone probe which numbers exist.
        var mobileTaken = await dbContext.Users.AnyAsync(u => u.Mobile == mobile && !u.IsDeleted, ct);
        if (mobileTaken)
        {
            return ValidationProblem("این شمارهٔ همراه قبلاً ثبت شده است. اگر رمزتان را فراموش کرده‌اید با پشتیبانی تماس بگیرید.");
        }

        var sent = await signupOtpService.SendAsync(mobile, ct);
        if (!sent)
        {
            return ValidationProblem("ارسال پیامک ناموفق بود. لحظاتی بعد دوباره تلاش کنید.");
        }

        return Ok(new { sent = true });
    }

    [HttpPost]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<AgencySignupResultDto>> Signup(AgencySignupRequest request, CancellationToken ct)
    {
        if (!await IsSignupOpenAsync(ct))
        {
            return SignupClosed();
        }

        var mobile = MobileNumberValidator.Normalize(request.ManagerMobile);
        if (string.IsNullOrWhiteSpace(request.AgencyName) || string.IsNullOrWhiteSpace(request.ManagerFullName))
        {
            return ValidationProblem("نام نمایندگی و نام مدیر الزامی است.");
        }

        if (!MobileNumberValidator.IsValid(mobile))
        {
            return ValidationProblem("شمارهٔ همراه نامعتبر است.");
        }

        var mobileTaken = await dbContext.Users.AnyAsync(u => u.Mobile == mobile && !u.IsDeleted, ct);
        if (mobileTaken)
        {
            return ValidationProblem("این شمارهٔ همراه قبلاً ثبت شده است.");
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            return ValidationProblem("رمز عبور باید حداقل ۸ کاراکتر باشد.");
        }

        if (request.Province is { } province && !IranProvinces.All.Contains(province))
        {
            return ValidationProblem("استان انتخاب‌شده معتبر نیست.");
        }

        if (string.IsNullOrWhiteSpace(request.OtpCode) || !signupOtpService.Verify(mobile, request.OtpCode.Trim()))
        {
            return ValidationProblem("کد تأیید نامعتبر یا منقضی است.");
        }

        var hq = await dbContext.Organizations.FirstOrDefaultAsync(o => o.Level == OrganizationLevel.Headquarters, ct);
        if (hq is null)
        {
            return ValidationProblem("سامانه هنوز راه‌اندازی نشده است.");
        }

        var code = await GenerateUniqueCodeAsync(ct);
        var role = await GetOrCreateDefaultManagerRoleAsync(ct);

        var agency = new Organization
        {
            Level = OrganizationLevel.Agency,
            ParentId = hq.Id,
            Code = code,
            Name = request.AgencyName.Trim(),
            Province = NormalizeOptional(request.Province),
            City = NormalizeOptional(request.City),
            InsurerName = NormalizeOptional(request.InsurerName),
            IsActive = false,
        };
        dbContext.Organizations.Add(agency);
        await dbContext.SaveChangesAsync(ct);

        var manager = new AppUser
        {
            FullName = request.ManagerFullName.Trim(),
            Mobile = mobile,
            PasswordHash = passwordHasher.Hash(request.Password),
            IsActive = true,
        };
        dbContext.Users.Add(manager);
        await dbContext.SaveChangesAsync(ct);

        dbContext.UserOrgRoles.Add(new UserOrgRole { UserId = manager.Id, OrganizationId = agency.Id, RoleId = role.Id });
        await dbContext.SaveChangesAsync(ct);

        WriteSignupAudit(agency, manager);

        return Created(string.Empty, new AgencySignupResultDto(
            "ثبت‌نام ثبت شد. پس از تأیید مالک سامانه می‌توانید با همین شمارهٔ همراه وارد شوید."));
    }

    /// <summary>The signup audit row is RLS-scoped like every AuditEntry, so the agency's session
    /// context must be set for the write and restored after — same mechanism DevSeeder uses to
    /// insert into a brand-new agency (rules 27-29: written in the same unit of work).</summary>
    private void WriteSignupAudit(Organization agency, AppUser manager)
    {
        var previousAgency = AgencyContext.Current;
        AgencyContext.Current = agency.Id;
        try
        {
            dbContext.AuditEntries.Add(new AuditEntry
            {
                AgencyId = agency.Id,
                UserId = Guid.Empty,
                UserDisplayName = "ثبت‌نام خودکار",
                EntityType = nameof(Organization),
                EntityId = agency.Id,
                PolicyId = Guid.Empty,
                Action = AuditAction.Created,
                Description = $"ثبت‌نام خودکار نمایندگی «{agency.Name}» — مدیر: {manager.FullName} ({manager.Mobile})",
                OccurredAt = DateTimeOffset.UtcNow,
                IpAddress = CurrentRequestContext.IpAddress,
            });
            dbContext.SaveChanges();
        }
        finally
        {
            AgencyContext.Current = previousAgency;
        }
    }

    /// <summary>The owner's DB-backed switch (PlatformSignupSettings). Checked per request, so
    /// flipping it in «مدیریت نمایندگی‌ها» is instant. No row yet = closed — a fresh install must
    /// never discover it left the front door open by default.</summary>
    private async Task<bool> IsSignupOpenAsync(CancellationToken ct) =>
        await dbContext.PlatformSignupSettings
            .AsNoTracking()
            .Select(s => (bool?)s.AllowAgencySignup)
            .FirstOrDefaultAsync(ct) == true;

    /// <summary>Auto-generated because the signer does not know (and must not choose) the
    /// agency's internal code; the owner can rename it later like any other agency field.</summary>
    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var candidate = $"SU-{Guid.NewGuid():N}"[..11];
            if (!await dbContext.Organizations.AnyAsync(o => o.Code == candidate && !o.IsDeleted, ct))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("تولید کد یکتای نمایندگی ناموفق بود.");
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

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private ActionResult SignupClosed() => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "ثبت‌نام نمایندگی در حال حاضر فعال نیست.",
    });

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
