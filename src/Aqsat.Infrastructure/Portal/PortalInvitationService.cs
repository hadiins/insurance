using System.Security.Cryptography;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Application.Payments;
using Aqsat.Application.Sms;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aqsat.Infrastructure.Portal;

/// <summary>Thrown for every caller-visible portal failure — surfaces as a 400/404/409 with its
/// Persian message, never an empty catch (CLAUDE.md rule 15).</summary>
public sealed class PortalInvitationException(string message) : Exception(message);

public sealed record PortalInvitationResult(CustomerPortalInvitation Invitation, bool SmsSent);

public sealed record PortalPaymentResult(
    /// <summary>Null only when the gateway wants a browser redirect — nothing settled yet.</summary>
    decimal? PaidAmountToman,
    DateTimeOffset? PaidAtUtc,
    /// <summary>Set for a real PSP (ZarinPal/GooyaPay): send the customer's browser here; the
    /// callback endpoint completes the flow. Null for Mock and for a completed inline payment.</summary>
    string? RedirectUrl = null);

/// <summary>
/// The link-issuing and payment half of the customer portal (docs/CUSTOMER-PORTAL-SPEC.md §3).
/// Operator actions run inside the caller's RLS scope (AgencyContext set by
/// ScopeResolutionMiddleware); the public token flow resolves the agency first through the
/// RLS-exempt PortalInvitationTokenIndex table — the one narrow lookup an anonymous visitor
/// gets — then works scoped like any other read.
/// </summary>
public sealed class PortalInvitationService(
    AppDbContext dbContext,
    ISmsSender smsSender,
    GatewayPaymentCoordinator coordinator,
    IScopeGuard scopeGuard,
    PolicyVerificationService policyVerificationService,
    IConfiguration configuration,
    ILogger<PortalInvitationService> logger)
{
    public async Task<PortalInvitationResult> CreateForCustomerAsync(
        Guid customerId, Guid currentUserId, CancellationToken ct = default)
    {
        var agencyId = Aqsat.Infrastructure.Persistence.AgencyContext.Current
            ?? throw new PortalInvitationException("دامنهٔ نمایندگی نامعتبر است.");

        var settings = await dbContext.OrgSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == agencyId, ct);
        var defaults = new OrgSettings();
        var portalEnabled = settings?.CustomerPortalEnabled ?? defaults.CustomerPortalEnabled;
        if (!portalEnabled)
        {
            throw new PortalInvitationException(
                "پورتال مشتری برای این نمایندگی فعال نیست. ابتدا آن را در تنظیمات نمایندگی فعال کنید.");
        }

        // Rule 11 — RLS alone would silently accept an out-of-scope CustomerId.
        await scopeGuard.EnsureExistsInScopeAsync<Customer>(customerId, ct);

        var customer = await dbContext.Customers.AsNoTracking()
            .FirstAsync(c => c.Id == customerId, ct);
        if (string.IsNullOrWhiteSpace(customer.Mobile))
        {
            throw new PortalInvitationException("شمارهٔ همراه مشتری ثبت نشده است؛ برای ارسال لینک ابتدا آن را کامل کنید.");
        }

        // The fee buys the credit inquiries, and the inquiries need the national ID — refusing
        // up front beats collecting the fee and failing the inquiry after (owner decision 2026-09-03).
        if (string.IsNullOrWhiteSpace(customer.NationalId))
        {
            throw new PortalInvitationException("کد ملی مشتری ثبت نشده است؛ استعلام اعتباری بدون کد ملی ممکن نیست.");
        }

        var activeInvitation = await dbContext.CustomerPortalInvitations.AsNoTracking()
            .FirstOrDefaultAsync(
                i => i.CustomerId == customerId && i.Status == PortalInvitationStatus.Pending && !i.IsDeleted,
                ct);
        if (activeInvitation is not null)
        {
            throw new PortalInvitationException(
                $"برای این مشتری یک لینک فعال تا {activeInvitation.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC موجود است. ابتدا آن را تکمیل یا منتظر انقضا بمانید.");
        }

        var ttlHours = settings?.PortalInvitationTtlHours ?? defaults.PortalInvitationTtlHours;

        // The inquiry fee is an owner-account concern (owner decision 2026-09-01): collected
        // through the OWNER's gateway, so its amount lives on PlatformPaymentSettings — never on
        // the agency's settings. The default mirrors the entity's own initializer for a fresh row.
        var platformPayment = await dbContext.PlatformPaymentSettings.AsNoTracking()
            .FirstOrDefaultAsync(ct);
        var fee = platformPayment?.InquiryFeeToman ?? new Aqsat.Domain.PlatformPaymentSettings().InquiryFeeToman;

        var invitation = new CustomerPortalInvitation
        {
            AgencyId = agencyId,
            CustomerId = customerId,
            Token = GenerateToken(),
            InquiryFeeToman = fee,
            CreatedByUserId = currentUserId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(ttlHours),
            Status = PortalInvitationStatus.Pending,
        };
        dbContext.CustomerPortalInvitations.Add(invitation);
        // The RLS-exempt token→agency row, written in the same transaction as the invitation —
        // the anonymous portal resolves the agency from this, never from the RLS-scoped table.
        dbContext.PortalInvitationTokenIndex.Add(new PortalInvitationTokenIndex
        {
            AgencyId = agencyId,
            Token = invitation.Token,
            InvitationId = invitation.Id,
        });
        await dbContext.SaveChangesAsync(ct);

        var link = $"{configuration["Portal:PublicBaseUrl"]?.TrimEnd('/')}/portal/{invitation.Token}";
        var smsText =
            $"{customer.FullName} عزیز،\n" +
            "برای پرداخت کارمزد استعلام بیمه و تکمیل پروندهٔ خود، روی لینک زیر کلیک کنید:\n" +
            $"{link}\n" +
            $"اعتبار لینک: {ttlHours} ساعت.";

        var smsSent = false;
        try
        {
            smsSent = await smsSender.SendAsync(customer.Mobile!, smsText, agencyId, ct);
        }
        catch (Exception ex)
        {
            // Never an empty catch (rule 15) — the link exists and the operator can hand it over
            // manually, so this is surfaced as a warning, not swallowed.
            logger.LogWarning(ex, "Portal-invitation SMS failed for customer {CustomerId}.", customerId);
        }

        return new PortalInvitationResult(invitation, smsSent);
    }

    /// <summary>The invitation plus the agency scope it was resolved under — the caller needs
    /// the agency for any follow-up read, because the ambient scope is restored to its previous
    /// (anonymous) value once this call returns.</summary>
    public sealed record TokenLookupResult(CustomerPortalInvitation Invitation, Guid AgencyId);

    public async Task<TokenLookupResult> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        return await RunWithAgencyScopeAsync(token, ct, async (invitation, agencyId) =>
        {
            await ApplyLazyExpiryAsync(invitation, ct);
            return new TokenLookupResult(invitation, agencyId);
        });
    }

    /// <summary>Display name for the public portal page — pass the agency id from
    /// TokenLookupResult; the ambient scope is anonymous again by the time this is called.</summary>
    public async Task<string> GetCustomerDisplayNameAsync(Guid agencyId, Guid customerId, CancellationToken ct = default)
    {
        return await RunWithAgencyScopeAsync(agencyId, async () =>
            await dbContext.Customers.AsNoTracking()
                .Where(c => c.Id == customerId)
                .Select(c => c.FullName)
                .FirstAsync(ct));
    }

    public async Task<PortalPaymentResult> PayAsync(string token, string? customerIp, CancellationToken ct = default)
    {
        return await RunWithAgencyScopeAsync(token, ct, async (invitation, agencyId) =>
        {
            await ApplyLazyExpiryAsync(invitation, ct);

            if (invitation.Status == PortalInvitationStatus.Paid)
            {
                throw new PortalInvitationException("این کارمزد قبلاً پرداخت شده است.");
            }
            if (invitation.Status == PortalInvitationStatus.Expired)
            {
                throw new PortalInvitationException("اعتبار این لینک به پایان رسیده است. لطفاً از نمایندگی لینک جدید دریافت کنید.");
            }

            // Platform kill switch first (PlatformPaymentSettings.Enabled), then the provider and
            // its credential — the inquiry fee is collected through the OWNER's gateway
            // (PlatformPaymentSettings), so the agency's own AgentMerchantId is irrelevant here.
            var platformSettings = await dbContext.PlatformPaymentSettings.AsNoTracking()
                .FirstOrDefaultAsync(ct);
            if (platformSettings is null || !platformSettings.Enabled)
            {
                throw new PortalInvitationException("پرداخت آنلاین از سمت پلتفرم فعال نیست. لطفاً بعداً تلاش کنید یا با نمایندگی تماس بگیرید.");
            }

            if (platformSettings.Provider != PaymentProvider.Mock &&
                string.IsNullOrWhiteSpace(platformSettings.OwnerMerchantId))
            {
                throw new PortalInvitationException(
                    "شناسهٔ پذیرندهٔ پلتفرم ثبت نشده است؛ پرداخت ممکن نیست. با پشتیبانی تماس بگیرید.");
            }

            var customer = await dbContext.Customers.AsNoTracking()
                .FirstAsync(c => c.Id == invitation.CustomerId, ct);

            // Mock resolves the charge inline and never redirects, so no callback can ever arrive —
            // demanding a callback base URL here would break the Mock provider's zero-configuration
            // contract (a fresh install must complete this flow before any PSP account exists).
            // A real PSP still fails fast here, before any request goes out, when no base is
            // resolvable (settings.CallbackBaseUrl empty and Portal:ApiPublicBaseUrl unset).
            var callbackUrl = platformSettings.Provider == PaymentProvider.Mock
                ? string.Empty
                : $"{await coordinator.ResolveCallbackBaseAsync(ct)}/api/portal/{invitation.Token}/callback";

            var charge = await coordinator.ChargeAsync(
                new GatewayChargeContext(
                    platformSettings.Provider, GatewayPurpose.InquiryFee, agencyId,
                    InvitationId: invitation.Id, CustomerId: invitation.CustomerId),
                new GatewayChargeRequest(
                    invitation.Token,
                    platformSettings.OwnerMerchantId ?? string.Empty,
                    invitation.InquiryFeeToman,
                    "پرداخت کارمزد استعلام",
                    callbackUrl,
                    Mobile: customer.Mobile,
                    FullName: customer.FullName),
                ct);

            if (charge.RequiresRedirect)
            {
                // A real PSP: nothing is settled — the callback endpoint finalises after verify.
                return new PortalPaymentResult(null, null, charge.RedirectUrl);
            }

            var settledAmount = await SettleFeeAsync(
                invitation, customer, agencyId, charge.SettledAmountToman ?? invitation.InquiryFeeToman, customerIp, ct);
            await FireInquiriesAsync(invitation, agencyId, ct);

            return new PortalPaymentResult(settledAmount, invitation.PaidAtUtc!.Value);
        });
    }

    /// <summary>The PSP's browser callback for the inquiry fee: resolves the Pending charge by the
    /// gateway's own reference inside the invitation's agency scope, verifies server-to-server
    /// (never trusting the callback's success flag), then settles with exactly the same money-moving
    /// path an inline fee payment uses — status/stage flips, same-transaction audit, and the
    /// inquiries fired the moment the fee lands. Returns the SPA redirect the controller bounces
    /// the customer's browser to.</summary>
    public async Task<string> FinalizeFeeCallbackAsync(
        string token, IReadOnlyDictionary<string, string> callbackParameters,
        string? customerIp, CancellationToken ct = default)
    {
        return await RunWithAgencyScopeAsync(token, ct, async (invitation, agencyId) =>
        {
            var transaction = await coordinator.ResolveCallbackTransactionAsync(
                invitation.Id, null, callbackParameters, ct);
            var data = coordinator.ReadCallback(transaction.Provider, callbackParameters);
            if (!data.SuccessFlagSet)
            {
                await coordinator.MarkCallbackFailedAsync(
                    transaction, "بازگشت از درگاه پرداخت موفق گزارش نشد.", ct);
                return PortalCallbackReturnUrl(token, "failed", "cancelled");
            }

            try
            {
                var outcome = await coordinator.VerifyAsync(transaction.Provider, transaction.GatewayReference, ct);
                var customer = await dbContext.Customers.AsNoTracking()
                    .FirstAsync(c => c.Id == invitation.CustomerId, ct);
                var settledAmount = await SettleFeeAsync(
                    invitation, customer, agencyId, outcome.SettledAmountToman, customerIp, ct);
                await FireInquiriesAsync(invitation, agencyId, ct);
                return PortalCallbackReturnUrl(token, "ok", $"{settledAmount:N0}");
            }
            catch (PortalInvitationException ex)
            {
                // Verify refused the payment — the row is already Failed/Expired in the database,
                // and the customer must see the Persian reason on the SPA page, not a bare error.
                return PortalCallbackReturnUrl(token, "failed", ex.Message);
            }
        });
    }

    /// <summary>The money-moving half of an inquiry fee, shared verbatim by the inline (mock /
    /// one-phase) path and the PSP-callback path. Marks the invitation paid, stage FeePaid, and
    /// writes the same-transaction audit row. Returns the settled amount.</summary>
    private async Task<decimal> SettleFeeAsync(
        CustomerPortalInvitation invitation, Customer customer, Guid agencyId,
        decimal settledAmount, string? customerIp, CancellationToken ct)
    {
        if (settledAmount <= 0)
        {
            throw new PortalInvitationException("مبلغ پرداخت نامعتبر است؛ با پشتیبانی تماس بگیرید.");
        }

        invitation.Status = PortalInvitationStatus.Paid;
        // A verification chain records the fee landing in its own stage too — the retry
        // endpoints key off FeePaid, so skipping this would strand a paid-but-failed chain at
        // FeePending with no way to re-fire the inquiries. A standalone (PolicyId null) link
        // keeps the same convention so the operator retry path can find it.
        invitation.Stage = PolicyVerificationStage.FeePaid;
        invitation.PaidAtUtc = DateTimeOffset.UtcNow;
        invitation.PaidAmountToman = settledAmount;

        // Same-transaction audit (rules 27-29) — customer-side action, no authenticated user, so
        // the actor is the portal itself with the customer's mobile for traceability.
        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = agencyId,
            UserId = invitation.CreatedByUserId,
            UserDisplayName = "پورتال مشتری",
            EntityType = nameof(CustomerPortalInvitation),
            EntityId = invitation.Id,
            PolicyId = Guid.Empty,
            Action = AuditAction.PaymentRecorded,
            Description = $"پرداخت کارمزد استعلام ({invitation.PaidAmountToman:N0} تومان) از طریق پورتال مشتری ({customer.Mobile})",
            OccurredAt = DateTimeOffset.UtcNow,
            IpAddress = customerIp,
        });
        await dbContext.SaveChangesAsync(ct);
        return settledAmount;
    }

    /// <summary>Fires the credit inquiries after a settled fee, logging (never throwing) a
    /// failure — shared by the inline and callback paths (see PayAsync's remark).</summary>
    private async Task FireInquiriesAsync(
        CustomerPortalInvitation invitation, Guid agencyId, CancellationToken ct)
    {
        var inquiry = await policyVerificationService.RunInquiriesAsync(invitation, agencyId, ct);
        if (!inquiry.Succeeded)
        {
            logger.LogWarning(
                "Credit inquiries after fee payment failed for invitation {InvitationId} (policy {PolicyId}): {Error}",
                invitation.Id, invitation.PolicyId, inquiry.Error);
        }
    }

    /// <summary>Where the PSP's browser is bounced after a fee payment: back onto the SPA's own
    /// /portal/{token} page with a machine-readable result the page renders.</summary>
    private string PortalCallbackReturnUrl(string token, string payState, string detail) =>
        $"{configuration["Portal:PublicBaseUrl"]?.TrimEnd('/')}/portal/{Uri.EscapeDataString(token)}" +
        $"?pay={payState}&detail={Uri.EscapeDataString(detail)}";

    /// <summary>Runs work inside the invitation's agency scope: resolves the agency via the
    /// RLS-exempt PortalInvitationTokenIndex (the one token-keyed lookup an anonymous visitor
    /// gets), then reopens the connection so the session-context interceptor stamps the resolved
    /// agency, and keeps both the ambient scope and the open connection alive for the whole unit
    /// of work — every read, expiry flip and SaveChanges inside stays RLS-scoped to that agency.</summary>
    private async Task<TResult> RunWithAgencyScopeAsync<TResult>(
        string token, CancellationToken ct, Func<CustomerPortalInvitation, Guid, Task<TResult>> work)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 20 or > 43)
        {
            throw new PortalInvitationException("لینک نامعتبر است.");
        }

        // The one RLS-exempt read an anonymous visitor gets: token → agency from the
        // token-index table (SQL Server applies security-policy predicates even inside scalar
        // function bodies, so a fn_...Agency helper would fail under a NULL session context).
        var agencyId = await dbContext.PortalInvitationTokenIndex.AsNoTracking()
            .Where(t => t.Token == token)
            .Select(t => t.AgencyId)
            .FirstOrDefaultAsync(ct);

        if (agencyId == Guid.Empty)
        {
            throw new PortalInvitationException("لینک یافت نشد یا اعتبار آن به پایان رسیده است.");
        }


        var previousAgency = Aqsat.Infrastructure.Persistence.AgencyContext.Current;
        Aqsat.Infrastructure.Persistence.AgencyContext.Current = agencyId;
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
            Aqsat.Infrastructure.Persistence.AgencyContext.Current = previousAgency;
        }
    }

    private async Task<TResult> RunWithAgencyScopeAsync<TResult>(Guid agencyId, Func<Task<TResult>> work)
    {
        var previousAgency = Aqsat.Infrastructure.Persistence.AgencyContext.Current;
        Aqsat.Infrastructure.Persistence.AgencyContext.Current = agencyId;
        try
        {
            await dbContext.Database.OpenConnectionAsync();
            try
            {
                return await work();
            }
            finally
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
        finally
        {
            Aqsat.Infrastructure.Persistence.AgencyContext.Current = previousAgency;
        }
    }

    /// <summary>Expiry is applied lazily on access and persisted — an Expired row is immutable
    /// afterwards, so no background sweep is needed.</summary>
    private async Task ApplyLazyExpiryAsync(CustomerPortalInvitation invitation, CancellationToken ct)
    {
        if (invitation.Status == PortalInvitationStatus.Pending && invitation.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            invitation.Status = PortalInvitationStatus.Expired;
            await dbContext.SaveChangesAsync(ct);
        }
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
