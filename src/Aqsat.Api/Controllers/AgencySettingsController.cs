using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// "مشخصات نمایندگی" — the agency's own identity (name/city/insurer) plus every operational
/// parameter CLAUDE.md insists must be configurable rather than hard-coded: settlement deadline
/// days, lock scope, holiday shifting, installment/tab ceilings, reminder offsets, service fee
/// default, and the P&amp;L write-off / renewal-watch thresholds Tasks 15/16 introduced. No
/// OrgSettings row is ever created at agency-creation time in this codebase (every job/controller
/// reading it already falls back to hard-coded defaults via `?? default` for exactly that reason)
/// — GET mirrors those same defaults when the row doesn't exist yet, and PUT upserts it.
/// </summary>
[ApiController]
[Route("api/settings/agency")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class AgencySettingsController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AgencySettingsDto>> Get(CancellationToken ct)
    {
        var organization = await dbContext.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == currentUser.ActiveOrganizationId, ct);
        if (organization is null)
        {
            return NotFound();
        }

        var settings = await dbContext.OrgSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == currentUser.ActiveOrganizationId, ct);
        var defaults = new OrgSettings();

        var hasAnyPolicy = await dbContext.Policies.AsNoTracking().AnyAsync(ct);

        return Ok(new AgencySettingsDto(
            organization.Code, organization.Name, organization.City, organization.InsurerName,
            settings?.SettlementDeadlineDays ?? defaults.SettlementDeadlineDays,
            (settings?.LockScope ?? defaults.LockScope).ToString(),
            (settings?.DueDateRule ?? defaults.DueDateRule).ToString(),
            settings?.ShiftOnHoliday ?? defaults.ShiftOnHoliday,
            settings?.MaxInstallments ?? defaults.MaxInstallments,
            settings?.ReminderDaysBefore ?? defaults.ReminderDaysBefore,
            settings?.MaxOpenTabs ?? defaults.MaxOpenTabs,
            settings?.DefaultServiceFee ?? defaults.DefaultServiceFee,
            (settings?.ServiceFeeMode ?? defaults.ServiceFeeMode).ToString(),
            settings?.DefaultWriteOffDays ?? defaults.DefaultWriteOffDays,
            settings?.RenewalAutoWatchLeadDays ?? defaults.RenewalAutoWatchLeadDays,
            organization.AgencyCode, organization.AgencyCode is not null && hasAnyPolicy));
    }

    /// <summary>docs/TASK-24-POLICY-NUMBER.md §4.3 — "پس از اولین بیمه‌نامه قابل تغییر نیست...
    /// در سطح سرویس قفل شود، نه فقط UI": once any policy exists for this agency, the code is
    /// permanently locked here regardless of what the UI allows.</summary>
    [HttpPut("agency-code")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult<AgencySettingsDto>> UpdateAgencyCode(UpdateAgencyCodeRequest request, CancellationToken ct)
    {
        var organization = await dbContext.Organizations.FirstOrDefaultAsync(o => o.Id == currentUser.ActiveOrganizationId, ct);
        if (organization is null)
        {
            return NotFound();
        }

        var hasAnyPolicy = await dbContext.Policies.AsNoTracking().AnyAsync(ct);
        if (organization.AgencyCode is not null && hasAnyPolicy)
        {
            return ValidationProblem("کد نمایندگی پس از ثبت اولین بیمه‌نامه قابل تغییر نیست.");
        }

        organization.AgencyCode = request.AgencyCode.Trim();
        await dbContext.SaveChangesAsync(ct);

        return await Get(ct);
    }

    [HttpPut]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult<AgencySettingsDto>> Update(UpdateAgencySettingsRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ValidationProblem("نام نمایندگی الزامی است.");
        }

        if (request.SettlementDeadlineDays <= 0 || request.MaxInstallments <= 0 || request.MaxOpenTabs <= 0)
        {
            return ValidationProblem("مهلت تسویه، سقف اقساط و سقف تب‌ها باید مثبت باشند.");
        }

        if (!Enum.TryParse<LockScope>(request.LockScope, ignoreCase: true, out var lockScope))
        {
            return ValidationProblem("محدودهٔ قفل نامعتبر است.");
        }

        if (!Enum.TryParse<ServiceFeeMode>(request.ServiceFeeMode, ignoreCase: true, out var serviceFeeMode))
        {
            return ValidationProblem("حالت کارمزد خدمات نامعتبر است.");
        }

        if (!IsValidReminderOffsets(request.ReminderDaysBefore))
        {
            return ValidationProblem("روزهای یادآوری باید فهرستی از اعداد جدا‌شده با ویرگول باشد، مثلاً «7,3,0».");
        }

        var organization = await dbContext.Organizations.FirstOrDefaultAsync(o => o.Id == currentUser.ActiveOrganizationId, ct);
        if (organization is null)
        {
            return NotFound();
        }

        organization.Name = request.Name.Trim();
        organization.City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim();
        organization.InsurerName = string.IsNullOrWhiteSpace(request.InsurerName) ? null : request.InsurerName.Trim();

        var settings = await dbContext.OrgSettings.FirstOrDefaultAsync(s => s.OrganizationId == currentUser.ActiveOrganizationId, ct);
        if (settings is null)
        {
            settings = new OrgSettings { OrganizationId = currentUser.ActiveOrganizationId };
            dbContext.OrgSettings.Add(settings);
        }

        settings.SettlementDeadlineDays = request.SettlementDeadlineDays;
        settings.LockScope = lockScope;
        settings.ShiftOnHoliday = request.ShiftOnHoliday;
        settings.MaxInstallments = request.MaxInstallments;
        settings.ReminderDaysBefore = request.ReminderDaysBefore.Trim();
        settings.MaxOpenTabs = request.MaxOpenTabs;
        settings.DefaultServiceFee = request.DefaultServiceFee;
        settings.ServiceFeeMode = serviceFeeMode;
        settings.DefaultWriteOffDays = request.DefaultWriteOffDays;
        settings.RenewalAutoWatchLeadDays = request.RenewalAutoWatchLeadDays;

        await dbContext.SaveChangesAsync(ct);

        return await Get(ct);
    }

    /// <summary>«تنظیمات درگاه پرداخت» — the gateway half of the agency settings as its own
    /// endpoint. The agency's gateway receives ONLY its own customers' down payments and
    /// installments; the inquiry fee goes through the owner's platform gateway (two independent
    /// accounts that must never interfere — owner decision 2026-09-01).</summary>
    [HttpGet("payment-gateway")]
    public async Task<ActionResult<AgencyPaymentGatewayDto>> GetPaymentGateway(CancellationToken ct)
    {
        var organization = await dbContext.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == currentUser.ActiveOrganizationId, ct);
        if (organization is null)
        {
            return NotFound();
        }

        var settings = await dbContext.OrgSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == currentUser.ActiveOrganizationId, ct);
        var defaults = new OrgSettings();

        return Ok(new AgencyPaymentGatewayDto(
            organization.Name, organization.Code,
            (settings?.PaymentProvider ?? defaults.PaymentProvider).ToString(),
            settings?.CustomerPortalEnabled ?? defaults.CustomerPortalEnabled,
            !string.IsNullOrWhiteSpace(settings?.AgentMerchantId),
            Mask(settings?.AgentMerchantId),
            settings?.PortalInvitationTtlHours ?? defaults.PortalInvitationTtlHours));
    }

    [HttpPut("payment-gateway")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult<AgencyPaymentGatewayDto>> UpdatePaymentGateway(
        UpdateAgencyPaymentGatewayRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<PaymentProvider>(request.PaymentProvider, ignoreCase: true, out var paymentProvider))
        {
            return ValidationProblem("درگاه پرداخت نامعتبر است.");
        }

        if (request.PortalInvitationTtlHours <= 0)
        {
            return ValidationProblem("مدت اعتبار لینک باید مثبت باشد.");
        }

        var settings = await dbContext.OrgSettings.FirstOrDefaultAsync(s => s.OrganizationId == currentUser.ActiveOrganizationId, ct);
        if (settings is null)
        {
            settings = new OrgSettings { OrganizationId = currentUser.ActiveOrganizationId };
            dbContext.OrgSettings.Add(settings);
        }

        settings.PaymentProvider = paymentProvider;
        settings.CustomerPortalEnabled = request.CustomerPortalEnabled;
        // Null/whitespace = keep the stored credential — same contract as the platform panel and
        // ApiIrSettings: an ordinary save can never wipe a working merchant ID.
        if (!string.IsNullOrWhiteSpace(request.AgentMerchantId))
        {
            settings.AgentMerchantId = request.AgentMerchantId.Trim();
        }
        settings.PortalInvitationTtlHours = request.PortalInvitationTtlHours;

        await dbContext.SaveChangesAsync(ct);

        return await GetPaymentGateway(ct);
    }

    /// <summary>«تنظیمات پنل پیامکی» — the agency's own api.ir key for sending SMS. Empty key =
    /// fall back to the platform-level key, so GET distinguishes "has its own" from "inheriting".</summary>
    [HttpGet("sms-panel")]
    public async Task<ActionResult<AgencySmsPanelDto>> GetSmsPanel(CancellationToken ct)
    {
        var organization = await dbContext.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == currentUser.ActiveOrganizationId, ct);
        if (organization is null)
        {
            return NotFound();
        }

        var settings = await dbContext.OrgSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == currentUser.ActiveOrganizationId, ct);

        return Ok(new AgencySmsPanelDto(
            organization.Name, organization.Code,
            !string.IsNullOrWhiteSpace(settings?.SmsApiKey),
            Mask(settings?.SmsApiKey)));
    }

    [HttpPut("sms-panel")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult<AgencySmsPanelDto>> UpdateSmsPanel(UpdateAgencySmsPanelRequest request, CancellationToken ct)
    {
        var settings = await dbContext.OrgSettings.FirstOrDefaultAsync(s => s.OrganizationId == currentUser.ActiveOrganizationId, ct);
        if (settings is null)
        {
            settings = new OrgSettings { OrganizationId = currentUser.ActiveOrganizationId };
            dbContext.OrgSettings.Add(settings);
        }

        // Null/whitespace = keep the stored credential — an ordinary save can never wipe a working key.
        if (!string.IsNullOrWhiteSpace(request.SmsApiKey))
        {
            settings.SmsApiKey = request.SmsApiKey.Trim();
        }

        await dbContext.SaveChangesAsync(ct);

        return await GetSmsPanel(ct);
    }

    /// <summary>Wipes every operational record for this agency — policies, installments,
    /// payments/allocations, collateral, and commission entries — via soft-delete (CLAUDE.md rule
    /// 7: no hard deletes, so this is reversible at the database level even though there is no undo
    /// UI). Customers and users are left untouched. Meant for clearing test data before real use,
    /// not a routine action — gated behind typing the agency's own Code back.</summary>
    [HttpDelete("data")]
    [Authorize(Policy = Permissions.SettingsWrite)]
    public async Task<ActionResult> ClearData(ClearAgencyDataRequest request, CancellationToken ct)
    {
        var organization = await dbContext.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == currentUser.ActiveOrganizationId, ct);
        if (organization is null)
        {
            return NotFound();
        }

        if (!string.Equals(request.ConfirmCode?.Trim(), organization.Code, StringComparison.Ordinal))
        {
            return ValidationProblem("کد تأیید با کد نمایندگی مطابقت ندارد.");
        }

        var agencyId = currentUser.ActiveOrganizationId;
        var now = DateTimeOffset.UtcNow;

        await dbContext.PaymentAllocations.Where(a => a.AgencyId == agencyId && !a.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDeleted, true).SetProperty(a => a.DeletedAt, now), ct);
        await dbContext.Payments.Where(p => p.AgencyId == agencyId && !p.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDeleted, true).SetProperty(p => p.DeletedAt, now), ct);
        await dbContext.CommissionEntries.Where(c => c.AgencyId == agencyId && !c.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted, true).SetProperty(c => c.DeletedAt, now), ct);
        await dbContext.Collaterals.Where(c => c.AgencyId == agencyId && !c.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted, true).SetProperty(c => c.DeletedAt, now), ct);
        await dbContext.Installments.Where(i => i.AgencyId == agencyId && !i.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsDeleted, true).SetProperty(i => i.DeletedAt, now), ct);
        await dbContext.Policies.Where(p => p.AgencyId == agencyId && !p.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDeleted, true).SetProperty(p => p.DeletedAt, now), ct);

        // ExecuteUpdateAsync above is a bulk SQL statement, bypassing AppDbContext's per-entity
        // SaveChangesAsync audit override (rule 29) entirely — a manual row here is the only way an
        // action this destructive still ends up in the audit trail. Guid.Empty stands in for
        // PolicyId since this touches every policy in the agency, not one.
        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = agencyId,
            UserId = currentUser.UserId,
            UserDisplayName = currentUser.DisplayName,
            EntityType = nameof(Organization),
            EntityId = agencyId,
            PolicyId = Guid.Empty,
            Action = AuditAction.Updated,
            Description = $"پاکسازی کامل دادهٔ نمایندگی «{organization.Name}» (بیمه‌نامه‌ها، اقساط، پرداخت‌ها، وثیقه‌ها)",
            OccurredAt = now,
            IpAddress = CurrentRequestContext.IpAddress,
        });
        await dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    private static bool IsValidReminderOffsets(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .All(part => int.TryParse(part, out var n) && n >= 0);

    /// <summary>Same masking rule as ApiIrSettingsController/PlatformPaymentController — the
    /// merchant ID is write-only through this API; GET never returns it in clear.</summary>
    private static string? Mask(string? merchantId) =>
        string.IsNullOrWhiteSpace(merchantId) ? null
        : merchantId.Length <= 8 ? "••••"
        : $"{merchantId[..4]}••••{merchantId[^4..]}";

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
