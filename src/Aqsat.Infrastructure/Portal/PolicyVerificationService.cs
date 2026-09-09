using Aqsat.Application.ApiIr;
using Aqsat.Application.Payments;
using Aqsat.Application.Sms;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aqsat.Infrastructure.Portal;

public sealed record PolicyVerificationResult(CustomerPortalInvitation Invitation, bool SmsSent);

public sealed record InquiryRunResult(bool Succeeded, string? Error);

/// <summary>Everything the public portal page renders for one token — stage extras are null for a
/// plain inquiry-fee link (PolicyId null) and for stages where the customer must not see them
/// yet (contract/installments only after the agency approves).</summary>
public sealed record PublicPortalStageInfo(
    CustomerPortalInvitation Invitation,
    Guid AgencyId,
    string CustomerDisplayName,
    string PolicyNumber,
    string ContractText,
    IReadOnlyList<(int SeqNo, DateOnly DueDate, decimal Amount)> Installments);

/// <summary>
/// The staged issuance-verification chain between wizard steps 3 and 4 (owner decision
/// 2026-09-01): fee payment → credit inquiries (api.ir UnpaidCheque + ActiveLoans) → agency
/// decision → customer contract approval → down payment. Operator actions run in the caller's
/// RLS scope; token actions resolve the agency through the RLS-exempt token index exactly like
/// PortalInvitationService. Rejection cancels the policy — «براش بیمه اقساطی تعریف نشود».
/// </summary>
public sealed class PolicyVerificationService(
    AppDbContext dbContext,
    IApiIrClient apiIrClient,
    ISmsSender smsSender,
    IEnumerable<IPaymentGateway> gateways,
    IConfiguration configuration,
    ILogger<PolicyVerificationService> logger)
{
    /// <summary>Credit-inquiry cache window (CLAUDE.md rule 26) — a report younger than this is
    /// reused by the wizard instead of re-queried, so the fee is charged at most once per
    /// customer per 30 days.</summary>
    public static readonly TimeSpan ReportReuseWindow = TimeSpan.FromDays(30);

    public async Task<PolicyVerificationResult> CreateForPolicyAsync(
        Guid policyId, Guid currentUserId, CancellationToken ct = default)
    {
        var agencyId = AgencyContext.Current
            ?? throw new PortalInvitationException("دامنهٔ نمایندگی نامعتبر است.");

        var policy = await dbContext.Policies.FirstOrDefaultAsync(p => p.Id == policyId, ct)
            ?? throw new PortalInvitationException("بیمه‌نامه یافت نشد.");

        if (!policy.IsInstallment)
        {
            throw new PortalInvitationException("این بیمه‌نامه نقدی است و نیازی به اعتبارسنجی اقساطی ندارد.");
        }

        if (policy.InstallmentCount == 0)
        {
            throw new PortalInvitationException("ابتدا زمان‌بندی اقساط (مرحلهٔ ۳) را تکمیل کنید.");
        }

        if (policy.Status != PolicyStatus.Active)
        {
            throw new PortalInvitationException("این بیمه‌نامه فعال نیست و نمی‌تواند فرایند اعتبارسنجی را آغاز کند.");
        }

        var customer = await dbContext.Customers.AsNoTracking()
            .FirstAsync(c => c.Id == policy.CustomerId, ct);
        if (string.IsNullOrWhiteSpace(customer.Mobile))
        {
            throw new PortalInvitationException("شمارهٔ همراه مشتری ثبت نشده است؛ برای ارسال لینک ابتدا آن را کامل کنید.");
        }

        if (string.IsNullOrWhiteSpace(customer.NationalId))
        {
            throw new PortalInvitationException("کد ملی مشتری ثبت نشده است؛ استعلام اعتباری بدون کد ملی ممکن نیست.");
        }

        var openChain = await dbContext.CustomerPortalInvitations.AsNoTracking()
            .AnyAsync(i => i.PolicyId == policyId
                && i.Stage != PolicyVerificationStage.Completed
                && i.Stage != PolicyVerificationStage.Rejected
                && !i.IsDeleted, ct);
        if (openChain)
        {
            throw new PortalInvitationException("برای این بیمه‌نامه یک فرایند اعتبارسنجی باز وجود دارد.");
        }

        var activeInvitation = await dbContext.CustomerPortalInvitations.AsNoTracking()
            .FirstOrDefaultAsync(
                i => i.CustomerId == customer.Id && i.Status == PortalInvitationStatus.Pending && !i.IsDeleted, ct);
        if (activeInvitation is not null)
        {
            throw new PortalInvitationException(
                $"برای این مشتری یک لینک فعال تا {activeInvitation.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC موجود است. ابتدا آن را تکمیل یا منتظر انقضا بمانید.");
        }

        var settings = await dbContext.OrgSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == agencyId, ct);
        var defaults = new OrgSettings();
        if (settings?.CustomerPortalEnabled is not true)
        {
            throw new PortalInvitationException(
                "پورتال مشتری برای این نمایندگی فعال نیست. ابتدا آن را در تنظیمات نمایندگی فعال کنید.");
        }

        var ttlHours = settings.PortalInvitationTtlHours != 0 ? settings.PortalInvitationTtlHours : defaults.PortalInvitationTtlHours;

        var platformPayment = await dbContext.PlatformPaymentSettings.AsNoTracking()
            .FirstOrDefaultAsync(ct);
        var fee = platformPayment?.InquiryFeeToman ?? new PlatformPaymentSettings().InquiryFeeToman;

        // A fresh report — standalone (customer-file portal link) or from a previous policy chain —
        // inside the 30-day cache window (CLAUDE.md rule 26) is reused instead of charging the fee
        // and re-querying api.ir: the chain starts at ReportReady with a zero fee and no SMS
        // (owner decision 2026-09-03). The report row itself is COPIED onto this policy with its
        // original RetrievedAtUtc preserved — the same snapshot pattern as InquiryFeeToman.
        var reuseCutoff = DateTimeOffset.UtcNow - ReportReuseWindow;
        var reusableReport = await dbContext.CreditReports.AsNoTracking()
            .Where(r => r.CustomerId == customer.Id
                && r.RawSuccess
                && r.RetrievedAtUtc >= reuseCutoff)
            .OrderByDescending(r => r.RetrievedAtUtc)
            .ThenByDescending(r => r.BizId)
            .FirstOrDefaultAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var invitation = new CustomerPortalInvitation
        {
            // Client-side key: the reuse path's audit row (written in this same SaveChanges)
            // references the invitation by Id, which a NEWSEQUENTIALID() key only gets after the
            // insert (CLAUDE.md rule 1 allows either generator).
            Id = SequentialGuidGenerator.Next(),
            AgencyId = agencyId,
            CustomerId = customer.Id,
            PolicyId = policy.Id,
            Token = GenerateToken(),
            InquiryFeeToman = reusableReport is null ? fee : 0,
            DownPaymentAmountToman = policy.DownPayment,
            CreatedByUserId = currentUserId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(ttlHours),
            Status = reusableReport is null ? PortalInvitationStatus.Pending : PortalInvitationStatus.Paid,
            Stage = reusableReport is null ? PolicyVerificationStage.FeePending : PolicyVerificationStage.ReportReady,
        };
        if (reusableReport is not null)
        {
            // Fee "settled" at zero at creation — Status=Paid also keeps the link immune to the
            // lazy Pending→Expired flip, and the customer still needs it for contract approval and
            // the down payment.
            invitation.PaidAtUtc = now;
            invitation.PaidAmountToman = 0;
        }
        dbContext.CustomerPortalInvitations.Add(invitation);
        dbContext.PortalInvitationTokenIndex.Add(new PortalInvitationTokenIndex
        {
            AgencyId = agencyId,
            Token = invitation.Token,
            InvitationId = invitation.Id,
        });

        if (reusableReport is not null)
        {
            dbContext.CreditReports.Add(new CreditReport
            {
                AgencyId = agencyId,
                PolicyId = policy.Id,
                CustomerId = reusableReport.CustomerId,
                ChequeCount = reusableReport.ChequeCount,
                ChequeSumAmountToman = reusableReport.ChequeSumAmountToman,
                ChequeSumBouncedAmountToman = reusableReport.ChequeSumBouncedAmountToman,
                ActiveLoansCount = reusableReport.ActiveLoansCount,
                LoanTotalAmountToman = reusableReport.LoanTotalAmountToman,
                LoanDebtTotalAmountToman = reusableReport.LoanDebtTotalAmountToman,
                LoanPastExpiredTotalAmountToman = reusableReport.LoanPastExpiredTotalAmountToman,
                LoanDeferredTotalAmountToman = reusableReport.LoanDeferredTotalAmountToman,
                LoanSuspiciousTotalAmountToman = reusableReport.LoanSuspiciousTotalAmountToman,
                LoanDishonoredAmountToman = reusableReport.LoanDishonoredAmountToman,
                RawSuccess = reusableReport.RawSuccess,
                RetrievedAtUtc = reusableReport.RetrievedAtUtc,
            });
            dbContext.AuditEntries.Add(new AuditEntry
            {
                AgencyId = agencyId,
                UserId = currentUserId,
                UserDisplayName = "نمایندگی",
                EntityType = nameof(CustomerPortalInvitation),
                EntityId = invitation.Id,
                PolicyId = policy.Id,
                Action = AuditAction.PolicyVerification,
                Description =
                    $"بازیافت استعلام اعتباری قبلی ({reusableReport.RetrievedAtUtc:yyyy-MM-dd HH:mm} UTC) برای بیمه‌نامهٔ {policy.PolicyNumber} — بدون کارمزد و استعلام مجدد",
                OccurredAt = now,
            });
            await dbContext.SaveChangesAsync(ct);
            return new PolicyVerificationResult(invitation, false);
        }
        await dbContext.SaveChangesAsync(ct);

        var smsSent = false;
        try
        {
            smsSent = await smsSender.SendAsync(customer.Mobile!, BuildLinkSms(customer.FullName, invitation.Token, ttlHours), agencyId, ct);
        }
        catch (Exception ex)
        {
            // Never an empty catch (rule 15) — the link exists and the operator can hand it over
            // manually, so this is surfaced as a warning, not swallowed.
            logger.LogWarning(ex, "Policy-verification SMS failed for policy {PolicyId}.", policyId);
        }

        return new PolicyVerificationResult(invitation, smsSent);
    }

    /// <summary>The wizard's step-3.5 view: the policy's open chain and its credit report, if any.
    /// A null Invitation means no chain was ever started.</summary>
    public async Task<(CustomerPortalInvitation? Invitation, CreditReport? Report)> GetStatusAsync(
        Guid policyId, CancellationToken ct = default)
    {
        var invitation = await dbContext.CustomerPortalInvitations.AsNoTracking()
            .Where(i => i.PolicyId == policyId && !i.IsDeleted)
            .OrderByDescending(i => i.BizId)
            .FirstOrDefaultAsync(ct);

        var report = invitation is null
            ? null
            : await dbContext.CreditReports.AsNoTracking()
                .Where(r => r.PolicyId == policyId)
                .OrderByDescending(r => r.BizId)
                .FirstOrDefaultAsync(ct);

        return (invitation, report);
    }

    /// <summary>Runs both credit inquiries for an invitation whose fee was just paid — a policy
    /// chain (invitation.PolicyId set) or a standalone customer-file link (PolicyId null, owner
    /// decision 2026-09-03). Callers must already hold the invitation's agency scope
    /// (PortalInvitationService.PayAsync's open connection, or the retry endpoint's ambient
    /// operator scope). A rejected call leaves the stage at FeePaid and returns the Persian
    /// error — the fee is gone but the chain is retryable (POST /verification/retry-inquiries);
    /// a sandboxed call yields a report with RawSuccess=false so the representative sees "no
    /// real data" instead of silent zeros.</summary>
    public async Task<InquiryRunResult> RunInquiriesAsync(
        CustomerPortalInvitation invitation, Guid agencyId, CancellationToken ct = default)
    {
        var policy = invitation.PolicyId is null
            ? null
            : await dbContext.Policies.AsNoTracking()
                .FirstAsync(p => p.Id == invitation.PolicyId, ct);
        var customer = await dbContext.Customers.AsNoTracking()
            .FirstAsync(c => c.Id == invitation.CustomerId, ct);

        var cheque = await apiIrClient.UnpaidChequeAsync(customer.NationalId!, agencyId, ct);
        var loans = await apiIrClient.ActiveLoansAsync(customer.NationalId!, agencyId, ct);

        if (cheque is null || loans is null)
        {
            logger.LogWarning(
                "Credit inquiries for policy {PolicyId} were rejected by api.ir (cheque={ChequeOk}, loans={LoansOk}).",
                invitation.PolicyId, cheque is not null, loans is not null);
            return new InquiryRunResult(
                false, "استعلام اعتباری از سرویس استعلام رد شد. کلید یا اعتبار api.ir را بررسی کنید و دوباره تلاش کنید.");
        }

        var report = new CreditReport
        {
            // Client-side key — the inquiry audit row references it by Id in this same
            // SaveChanges (rule 29); a database-generated key would leave the audit pointing at
            // EF's empty placeholder.
            Id = SequentialGuidGenerator.Next(),
            AgencyId = agencyId,
            PolicyId = invitation.PolicyId,
            CustomerId = invitation.CustomerId,
            ChequeCount = cheque.Count,
            ChequeSumAmountToman = RialToToman(cheque.SumAmountRial),
            ChequeSumBouncedAmountToman = RialToToman(cheque.SumBouncedAmountRial),
            ActiveLoansCount = loans.Count,
            LoanTotalAmountToman = RialToToman(loans.TotalAmountRial),
            LoanDebtTotalAmountToman = RialToToman(loans.DebtTotalAmountRial),
            LoanPastExpiredTotalAmountToman = RialToToman(loans.PastExpiredTotalAmountRial),
            LoanDeferredTotalAmountToman = RialToToman(loans.DeferredTotalAmountRial),
            LoanSuspiciousTotalAmountToman = RialToToman(loans.SuspiciousTotalAmountRial),
            LoanDishonoredAmountToman = RialToToman(loans.DishonoredRial),
            RawSuccess = cheque.Count is not null && loans.Count is not null,
            RetrievedAtUtc = DateTimeOffset.UtcNow,
        };
        dbContext.CreditReports.Add(report);

        invitation.Stage = PolicyVerificationStage.ReportReady;

        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = agencyId,
            UserId = Guid.Empty,
            UserDisplayName = "سیستم",
            EntityType = nameof(CreditReport),
            EntityId = report.Id,
            PolicyId = invitation.PolicyId ?? Guid.Empty,
            Action = AuditAction.PolicyVerification,
            Description = policy is not null
                ? $"استعلام اعتباری بیمه‌نامهٔ {policy.PolicyNumber} انجام شد"
                : $"استعلام اعتباری مستقل مشتری {customer.FullName} انجام شد (لینک پورتال پروندهٔ مشتری)",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync(ct);

        if (customer.Mobile is { Length: > 0 })
        {
            try
            {
                await smsSender.SendAsync(
                    customer.Mobile,
                    $"{customer.FullName} عزیز، استعلام اعتباری شما انجام شد و پروندهٔ شما در حال بررسی نمایندگی است.",
                    agencyId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Post-inquiry SMS failed for policy {PolicyId}.", invitation.PolicyId);
            }
        }

        return new InquiryRunResult(true, null);
    }

    /// <summary>The retry path for a paid-but-failed inquiry run (stage still FeePaid, fee paid).</summary>
    public async Task<InquiryRunResult> RetryInquiriesAsync(Guid policyId, CancellationToken ct = default)
    {
        var invitation = await GetOpenInvitationAsync(policyId, ct);
        if (invitation.Stage != PolicyVerificationStage.FeePaid)
        {
            return new InquiryRunResult(false, "برای تلاش مجدد، وضعیت باید «کارمزد پرداخت‌شده و استعلام ناموفق» باشد.");
        }

        var agencyId = invitation.AgencyId;
        var result = await RunInquiriesAsync(invitation, agencyId, ct);
        return result;
    }

    /// <summary>The retry path for a standalone (customer-file) link whose inquiries failed after
    /// the fee landed — same contract as RetryInquiriesAsync but keyed by invitation id, because a
    /// standalone link has no policy to address it by.</summary>
    public async Task<InquiryRunResult> RetryStandaloneInquiriesAsync(Guid invitationId, CancellationToken ct = default)
    {
        var invitation = await dbContext.CustomerPortalInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.PolicyId == null, ct);
        if (invitation is null)
        {
            throw new PortalInvitationException("لینک یافت نشد یا به بیمه‌نامه متصل است.");
        }

        if (invitation.Stage != PolicyVerificationStage.FeePaid)
        {
            return new InquiryRunResult(false, "برای تلاش مجدد، وضعیت باید «کارمزد پرداخت‌شده و استعلام ناموفق» باشد.");
        }

        return await RunInquiriesAsync(invitation, invitation.AgencyId, ct);
    }

    /// <summary>The customer's latest credit report — any PolicyId, standalone included — plus its
    /// reuse eligibility for the issuance wizard. Returns null when no report exists; the caller
    /// renders the empty state, never an error (rule 17 applies to scope, not to a genuinely
    /// report-less customer).</summary>
    public async Task<CreditReport?> GetLatestCustomerReportAsync(Guid customerId, CancellationToken ct = default)
    {
        return await dbContext.CreditReports.AsNoTracking()
            .Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.RetrievedAtUtc)
            .ThenByDescending(r => r.BizId)
            .FirstOrDefaultAsync(ct);
    }

    public static bool IsReusable(CreditReport report) =>
        report.RawSuccess && report.RetrievedAtUtc >= DateTimeOffset.UtcNow - ReportReuseWindow;

    /// <summary>Approve keeps the chain moving; reject is terminal and cancels the policy with it.</summary>
    public async Task<PolicyVerificationResult> AgencyDecisionAsync(
        Guid policyId, bool approve, Guid currentUserId, string userDisplayName, CancellationToken ct = default)
    {
        var invitation = await GetOpenInvitationAsync(policyId, ct);
        var policy = await dbContext.Policies.FirstAsync(p => p.Id == policyId, ct);

        if (invitation.Stage != PolicyVerificationStage.ReportReady)
        {
            throw new PortalInvitationException("برای تصمیم‌گیری، ابتدا گزارش اعتباری باید آماده شود.");
        }

        var customer = await dbContext.Customers.AsNoTracking()
            .FirstAsync(c => c.Id == invitation.CustomerId, ct);

        invitation.Stage = approve ? PolicyVerificationStage.AgencyApproved : PolicyVerificationStage.Rejected;
        invitation.AgencyDecisionAtUtc = DateTimeOffset.UtcNow;
        invitation.AgencyDecisionByUserId = currentUserId;

        if (!approve)
        {
            policy.Status = PolicyStatus.Cancelled;
        }

        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = invitation.AgencyId,
            UserId = currentUserId,
            UserDisplayName = userDisplayName,
            EntityType = nameof(CustomerPortalInvitation),
            EntityId = invitation.Id,
            PolicyId = policy.Id,
            Action = AuditAction.PolicyVerification,
            Description = approve
                ? $"تأیید اعتبارسنجی بیمه‌نامهٔ {policy.PolicyNumber}"
                : $"رد اعتبارسنجی و لغو بیمه‌نامهٔ {policy.PolicyNumber}",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync(ct);

        var smsSent = false;
        if (customer.Mobile is { Length: > 0 })
        {
            var link = $"{configuration["Portal:PublicBaseUrl"]?.TrimEnd('/')}/portal/{invitation.Token}";
            try
            {
                smsSent = await smsSender.SendAsync(
                    customer.Mobile,
                    approve
                        ? $"{customer.FullName} عزیز، درخواست بیمه‌اقساطی شما تأیید شد. برای مشاهدهٔ قرارداد و پرداخت پیش‌پرداخت روی لینک زیر کلیک کنید:\n{link}"
                        : $"{customer.FullName} عزیز، درخواست بیمه‌اقساطی شما تأیید نشد. برای اطلاعات بیشتر با نمایندگی تماس بگیرید.",
                    invitation.AgencyId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Agency-decision SMS failed for policy {PolicyId}.", policyId);
            }
        }

        return new PolicyVerificationResult(invitation, smsSent);
    }

    /// <summary>The customer accepts قوانین/قرارداد/اقساط on the portal — token-keyed, anonymous.</summary>
    public async Task<CustomerPortalInvitation> CustomerApproveAsync(string token, CancellationToken ct = default)
    {
        return await RunWithAgencyScopeAsync(token, ct, async (invitation, _) =>
        {
            if (invitation.PolicyId is null)
            {
                throw new PortalInvitationException("این لینک برای تأیید قرارداد بیمه‌نامه نیست.");
            }

            if (invitation.Stage == PolicyVerificationStage.Rejected)
            {
                throw new PortalInvitationException("درخواست بیمه‌اقساطی شما از سمت نمایندگی رد شده است.");
            }

            if (invitation.Stage != PolicyVerificationStage.AgencyApproved)
            {
                throw new PortalInvitationException("قرارداد هنوز برای تأیید شما آماده نیست.");
            }

            invitation.Stage = PolicyVerificationStage.CustomerApproved;
            invitation.CustomerApprovedAtUtc = DateTimeOffset.UtcNow;

            dbContext.AuditEntries.Add(new AuditEntry
            {
                AgencyId = invitation.AgencyId,
                UserId = invitation.CreatedByUserId,
                UserDisplayName = "پورتال مشتری",
                EntityType = nameof(CustomerPortalInvitation),
                EntityId = invitation.Id,
                PolicyId = invitation.PolicyId.Value,
                Action = AuditAction.PolicyVerification,
                Description = "تأیید قوانین و قرارداد اقساط توسط مشتری از طریق پورتال",
                OccurredAt = DateTimeOffset.UtcNow,
            });
            await dbContext.SaveChangesAsync(ct);
            return invitation;
        });
    }

    /// <summary>The chain's final hop: the customer pays the down payment online through the
    /// AGENCY's gateway (OrgSettings.PaymentProvider/AgentMerchantId — the inquiry fee went through
    /// the owner's). Records the Payment idempotently exactly like receive-down-payment (rule 24)
    /// and flips the down-payment commission slices Payable, so an online and a manual receipt land
    /// in the same state.</summary>
    public async Task<(decimal PaidAmountToman, DateTimeOffset PaidAtUtc)> PayDownPaymentAsync(
        string token, string? customerIp, CancellationToken ct = default)
    {
        return await RunWithAgencyScopeAsync(token, ct, async (invitation, agencyId) =>
        {
            if (invitation.PolicyId is null)
            {
                throw new PortalInvitationException("این لینک برای پرداخت پیش‌پرداخت بیمه‌نامه نیست.");
            }

            if (invitation.Stage == PolicyVerificationStage.Completed)
            {
                throw new PortalInvitationException("پیش‌پرداخت این بیمه‌نامه قبلاً پرداخت شده است.");
            }

            if (invitation.Stage != PolicyVerificationStage.CustomerApproved)
            {
                throw new PortalInvitationException("پرداخت پیش‌پرداخت پیش از تأیید قرارداد امکان‌پذیر نیست.");
            }

            var policy = await dbContext.Policies.FirstAsync(p => p.Id == invitation.PolicyId, ct);
            var amount = invitation.DownPaymentAmountToman ?? policy.DownPayment;
            if (amount <= 0)
            {
                throw new PortalInvitationException("این بیمه‌نامه پیش‌پرداختی ندارد.");
            }

            // The agency's own gateway, not the platform's — this money lands in the agency's account.
            var settings = await dbContext.OrgSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.OrganizationId == agencyId, ct);
            var provider = settings?.PaymentProvider ?? new OrgSettings().PaymentProvider;
            var merchantId = settings?.AgentMerchantId;
            var gateway = gateways.FirstOrDefault(g => g.Provider == provider)
                ?? throw new PortalInvitationException($"درگاه پرداخت «{provider}» پشتیبانی نمی‌شود.");
            if (provider != PaymentProvider.Mock && string.IsNullOrWhiteSpace(merchantId))
            {
                throw new PortalInvitationException(
                    "درگاه پرداخت نمایندگی تنظیم نشده است؛ با نمایندگی تماس بگیرید.");
            }

            var paidOn = DateOnly.FromDateTime(DateTimeOffset.UtcNow.LocalDateTime);
            var existing = await dbContext.Payments.AsNoTracking().FirstOrDefaultAsync(
                p => p.InstallmentIdHint == policy.Id && p.PaidOn == paidOn && p.Amount == amount, ct);
            if (existing is not null)
            {
                invitation.Stage = PolicyVerificationStage.Completed;
                invitation.DownPaymentPaidAtUtc ??= DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(ct);
                return (existing.Amount, invitation.DownPaymentPaidAtUtc!.Value);
            }

            var result = await gateway.ChargeAsync(
                invitation.Token, merchantId ?? string.Empty, amount,
                $"پرداخت پیش‌پرداخت بیمه‌نامهٔ {policy.PolicyNumber}", string.Empty, ct);
            if (!result.Succeeded)
            {
                throw new PortalInvitationException(result.FailureReason ?? "پرداخت ناموفق بود. لطفاً دوباره تلاش کنید.");
            }

            var settledAmount = result.PaidAmountToman ?? amount;
            var occurredAt = DateTimeOffset.UtcNow;
            var payment = new Payment
            {
                // Client-side key — the audit row below references it by Id in this same
                // SaveChanges; a database-generated key would leave the audit pointing at EF's
                // empty placeholder.
                Id = SequentialGuidGenerator.Next(),
                AgencyId = agencyId,
                CustomerId = invitation.CustomerId,
                InstallmentIdHint = policy.Id,
                Amount = settledAmount,
                PaidOn = paidOn,
                Method = WellKnownPaymentMethods.DownPayment,
                ReferenceNo = $"PORTAL-{invitation.Token[..12]}",
                MethodType = PaymentMethod.Online,
                RecordedByUserId = invitation.CreatedByUserId,
            };
            dbContext.Payments.Add(payment);

            dbContext.AuditEntries.Add(new AuditEntry
            {
                AgencyId = agencyId,
                UserId = invitation.CreatedByUserId,
                UserDisplayName = "پورتال مشتری",
                EntityType = nameof(Payment),
                EntityId = payment.Id,
                PolicyId = policy.Id,
                Action = AuditAction.PaymentRecorded,
                Description = $"پرداخت آنلاین پیش‌پرداخت بیمه‌نامهٔ {policy.PolicyNumber} ({settledAmount:N0} تومان) از طریق پورتال مشتری",
                OccurredAt = occurredAt,
                IpAddress = customerIp,
            });

            await FlipDownPaymentCommissionSlicesAsync(policy.Id, occurredAt, ct);

            invitation.Stage = PolicyVerificationStage.Completed;
            invitation.DownPaymentPaidAtUtc = occurredAt;
            await dbContext.SaveChangesAsync(ct);

            return (settledAmount, occurredAt);
        });
    }

    /// <summary>Everything the public portal page renders for one token: base invitation info plus
    /// the contract text and the real installment schedule, which only appear once the agency has
    /// approved. Anonymous — the token is the credential.</summary>
    public async Task<PublicPortalStageInfo> GetPublicStageAsync(string token, CancellationToken ct = default)
    {
        return await RunWithAgencyScopeAsync(token, ct, async (invitation, agencyId) =>
        {
            // Same lazy expiry the plain portal lookup applies — an Expired row is immutable after.
            if (invitation.Status == PortalInvitationStatus.Pending && invitation.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                invitation.Status = PortalInvitationStatus.Expired;
                await dbContext.SaveChangesAsync(ct);
            }

            var displayName = await dbContext.Customers.AsNoTracking()
                .Where(c => c.Id == invitation.CustomerId)
                .Select(c => c.FullName)
                .FirstAsync(ct);

            var contractText = InstallmentContractDefaults.DefaultText;
            var policyNumber = string.Empty;
            var installments = new List<(int SeqNo, DateOnly DueDate, decimal Amount)>();

            if (invitation.PolicyId is { } policyId
                && invitation.Stage is PolicyVerificationStage.AgencyApproved
                    or PolicyVerificationStage.CustomerApproved
                    or PolicyVerificationStage.Completed)
            {
                var agencyContract = await dbContext.OrgSettings.AsNoTracking()
                    .Where(s => s.OrganizationId == agencyId)
                    .Select(s => s.InstallmentContractText)
                    .FirstOrDefaultAsync(ct);
                if (!string.IsNullOrWhiteSpace(agencyContract))
                {
                    contractText = agencyContract;
                }

                policyNumber = await dbContext.Policies.AsNoTracking()
                    .Where(p => p.Id == policyId)
                    .Select(p => p.PolicyNumber)
                    .FirstAsync(ct);

                installments = await dbContext.Installments.AsNoTracking()
                    .Where(i => i.PolicyId == policyId)
                    .OrderBy(i => i.SeqNo)
                    .Select(i => new ValueTuple<int, DateOnly, decimal>(i.SeqNo, i.DueDate, i.Amount))
                    .ToListAsync(ct);
            }

            return new PublicPortalStageInfo(invitation, agencyId, displayName, policyNumber, contractText, installments);
        });
    }

    private async Task<CustomerPortalInvitation> GetOpenInvitationAsync(Guid policyId, CancellationToken ct)
    {
        var invitation = await dbContext.CustomerPortalInvitations
            .Where(i => i.PolicyId == policyId && !i.IsDeleted)
            .OrderByDescending(i => i.BizId)
            .FirstOrDefaultAsync(ct)
            ?? throw new PortalInvitationException("برای این بیمه‌نامه فرایند اعتبارسنجی آغاز نشده است.");

        if (invitation.Stage == PolicyVerificationStage.Rejected)
        {
            throw new PortalInvitationException("اعتبارسنجی این بیمه‌نامه رد شده و بیمه‌نامه لغو شده است.");
        }

        return invitation;
    }

    /// <summary>Mirrors receive-down-payment: money in hand flips the down-payment slice of both
    /// CommissionEntry (marketer) and AgencyCommissionEntry Payable.</summary>
    private async Task FlipDownPaymentCommissionSlicesAsync(Guid policyId, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var downCommissionEntry = await dbContext.CommissionEntries
            .FirstOrDefaultAsync(c => c.PolicyId == policyId && c.InstallmentId == null, ct);
        if (downCommissionEntry is { Status: CommissionStatus.Pending })
        {
            downCommissionEntry.Status = CommissionStatus.Payable;
            downCommissionEntry.EligibleAt = occurredAt;
        }

        var downAgencyEntry = await dbContext.AgencyCommissionEntries
            .FirstOrDefaultAsync(c => c.PolicyId == policyId && c.InstallmentId == null && !c.IsFullPolicySlice, ct);
        if (downAgencyEntry is { Status: CommissionStatus.Pending })
        {
            downAgencyEntry.Status = CommissionStatus.Payable;
            downAgencyEntry.EligibleAt = occurredAt;
        }
    }

    /// <summary>Identical scope pattern to PortalInvitationService.RunWithAgencyScopeAsync — the
    /// token resolves the agency through the one RLS-exempt read, then the whole unit of work runs
    /// scoped to it.</summary>
    private async Task<TResult> RunWithAgencyScopeAsync<TResult>(
        string token, CancellationToken ct, Func<CustomerPortalInvitation, Guid, Task<TResult>> work)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 20 or > 43)
        {
            throw new PortalInvitationException("لینک نامعتبر است.");
        }

        var agencyId = await dbContext.PortalInvitationTokenIndex.AsNoTracking()
            .Where(t => t.Token == token)
            .Select(t => t.AgencyId)
            .FirstOrDefaultAsync(ct);

        if (agencyId == Guid.Empty)
        {
            throw new PortalInvitationException("لینک یافت نشد یا اعتبار آن به پایان رسیده است.");
        }

        var previousAgency = AgencyContext.Current;
        AgencyContext.Current = agencyId;
        try
        {
            await dbContext.Database.OpenConnectionAsync(ct);
            try
            {
                var invitation = await dbContext.CustomerPortalInvitations
                    .FirstOrDefaultAsync(i => i.Token == token, ct)
                    ?? throw new PortalInvitationException("لینک یافت نشد یا اعتبار آن به پایان رسیده است.");
                return await work(invitation, agencyId);
            }
            finally
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
        finally
        {
            AgencyContext.Current = previousAgency;
        }
    }

    private string BuildLinkSms(string customerName, string token, int ttlHours)
    {
        var link = $"{configuration["Portal:PublicBaseUrl"]?.TrimEnd('/')}/portal/{token}";
        return
            $"{customerName} عزیز،\n" +
            "برای پرداخت کارمزد استعلام و تکمیل فرایند بیمه‌نامهٔ اقساطی خود، روی لینک زیر کلیک کنید:\n" +
            $"{link}\n" +
            $"اعتبار لینک: {ttlHours} ساعت.";
    }

    private static decimal? RialToToman(decimal? rial) =>
        rial is null ? null : rial / 10m;

    private static string GenerateToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
