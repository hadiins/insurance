using Aqsat.Application.Payments;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Portal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aqsat.Infrastructure.Payments;

/// <summary>
/// What the caller must do after phase one. Exactly one of the two branches is populated:
/// either <see cref="SettledAmountToman"/> (an inline gateway already moved the money — finalise
/// now, exactly as before the two-phase contract) or <see cref="RedirectUrl"/> (a real PSP — the
/// caller must send the customer's browser there and let the callback drive the verify).
/// </summary>
public sealed record GatewayChargeOutcome(
    bool RequiresRedirect, string? RedirectUrl, decimal? SettledAmountToman, string? GatewayReference);

/// <summary>
/// Everything the coordinator needs to persist a Pending GatewayTransaction when a redirect is
/// required: what is being paid for, in which agency, and which rows the callback will finalise.
/// </summary>
public sealed record GatewayChargeContext(
    PaymentProvider Provider,
    GatewayPurpose Purpose,
    Guid AgencyId,
    Guid? InvitationId = null,
    Guid? InstallmentId = null,
    Guid? PolicyId = null,
    Guid? CustomerId = null);

/// <summary>Everything the callback needs after a successful verify: the row (now carrying the
/// PSP's evidence) and the amount that was actually captured, which a real PSP may report slightly
/// different from the asked amount.</summary>
public sealed record GatewayVerificationOutcome(GatewayTransaction Transaction, decimal SettledAmountToman);

/// <summary>
/// The one place the two-phase gateway flow lives, so the three portal payment paths
/// (inquiry fee, down payment, installment) cannot drift apart in how they handle it.
///
/// On phase one it enforces each PSP's own amount range BEFORE a request goes out — an out-of-range
/// amount would otherwise return an opaque PSP rejection the customer cannot act on — and, when the
/// PSP hands back a reference, it records the Pending row the callback will later be resolved
/// against. Superseded Pending rows for the same subject are expired, so an abandoned authority can
/// never be replayed into a settlement later.
/// </summary>
public sealed class GatewayPaymentCoordinator(
    AppDbContext dbContext,
    IEnumerable<IPaymentGateway> gateways,
    IConfiguration configuration)
{
    /// <summary>How long a pending PSP charge stays usable. ZarinPal's own reverse window is 30
    /// minutes, so a reference older than that is of no use to anyone anyway; گویا پی publishes no
    /// figure, and the customer either completes on the PSP's page or abandons it.</summary>
    private static readonly TimeSpan ChargeWindow = TimeSpan.FromMinutes(30);

    public IPaymentGateway Resolve(PaymentProvider provider) =>
        gateways.FirstOrDefault(g => g.Provider == provider)
        ?? throw new PortalInvitationException($"درگاه پرداخت «{provider}» پشتیبانی نمی‌شود.");

    /// <summary>Normalises a PSP callback's parameters through the provider's own reader — the
    /// single place field-name knowledge stays behind (GooyaPay's PaymentStatus/Authority,
    /// ZarinPal's Status/Authority). The controller only merges query + form into a dictionary.
    /// Only usable on a live GatewayTransaction row (the provider comes from it), because an
    /// anonymous callback cannot be trusted to name its own provider.</summary>
    public GatewayCallbackData ReadCallback(
        PaymentProvider provider, IReadOnlyDictionary<string, string> parameters) =>
        Resolve(provider).ReadCallback(parameters);

    /// <summary>
    /// Resolves an anonymous PSP callback to its Pending GatewayTransaction. The provider cannot be
    /// taken from the callback (attacker-controllable), so every registered gateway's reader is
    /// tried: the first one whose extracted reference matches a Pending row whose OWN provider
    /// agrees with that reader wins — a mismatch (ZarinPal's reader recognising a GooyaPay
    /// authority string) is skipped, not trusted. Runs inside the caller's RLS scope, so the row
    /// can only ever resolve inside the agency the token fixed.
    /// </summary>
    public async Task<GatewayTransaction> ResolveCallbackTransactionAsync(
        Guid? invitationId, Guid? installmentId,
        IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default)
    {
        foreach (var gateway in gateways)
        {
            var data = gateway.ReadCallback(parameters);
            if (string.IsNullOrWhiteSpace(data.Reference))
            {
                continue;
            }

            var transaction = await dbContext.GatewayTransactions
                .FirstOrDefaultAsync(
                    t => t.GatewayReference == data.Reference
                        && t.Status == GatewayTransactionStatus.Pending
                        && (invitationId == null || t.InvitationId == invitationId)
                        && (installmentId == null || t.InstallmentId == installmentId), ct);

            if (transaction is not null && transaction.Provider == gateway.Provider)
            {
                return transaction;
            }
        }

        throw new PortalInvitationException(
            "تراکنش متناظر با این بازگشت از پرداخت یافت نشد. اگر مبلغی از حساب شما کسر شده، با نمایندگی تماس بگیرید.");
    }

    /// <summary>Marks a Pending transaction Failed when the PSP's callback says the customer
    /// abandoned or the gateway reported failure — an Expired/Failed row is immutable afterwards,
    /// and the next "pay" click starts a fresh charge (the coordinator supersedes stale rows).</summary>
    public async Task MarkCallbackFailedAsync(
        GatewayTransaction transaction, string reason, CancellationToken ct = default)
    {
        transaction.Status = GatewayTransactionStatus.Failed;
        transaction.FailureReason = Truncate(reason, 500);
        await dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The absolute base URL a real PSP must call back on. PlatformPaymentSettings.CallbackBaseUrl
    /// is the primary source and deliberately covers BOTH flows: one API deployment serves every
    /// agency, so the owner setting one value is what makes the agency-side flows work too (the
    /// field's own documentation says exactly this). The configuration fallback exists so a
    /// developer running locally can point at their own host without editing platform settings.
    ///
    /// Throws rather than returning an empty string: a blank CallbackURL is rejected by the PSP,
    /// and "no callback" would otherwise surface as a request failure at the customer's expense.
    /// </summary>
    public async Task<string> ResolveCallbackBaseAsync(CancellationToken ct = default)
    {
        var settings = await dbContext.PlatformPaymentSettings.AsNoTracking()
            .FirstOrDefaultAsync(ct);

        var baseUrl = settings?.CallbackBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = configuration["Portal:ApiPublicBaseUrl"];
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new PortalInvitationException(
                "آدرس بازگشت پرداخت (Callback URL) در تنظیمات درگاه پرداخت مالک ثبت نشده است؛ پرداخت آنلاین ممکن نیست.");
        }

        return baseUrl.TrimEnd('/');
    }

    /// <summary>Phase one. Throws PortalInvitationException (a caller-visible 400 with its Persian
    /// message) rather than returning a failure the caller must remember to translate.</summary>
    public async Task<GatewayChargeOutcome> ChargeAsync(
        GatewayChargeContext context, GatewayChargeRequest request, CancellationToken ct = default)
    {
        var gateway = Resolve(context.Provider);

        if (request.AmountToman < gateway.MinAmountToman || request.AmountToman > gateway.MaxAmountToman)
        {
            throw new PortalInvitationException(
                $"مبلغ {request.AmountToman:N0} تومان در درگاه «{context.Provider}» قابل پرداخت نیست. " +
                $"بازهٔ مجاز پرداخت در این درگاه از {gateway.MinAmountToman:N0} تا {gateway.MaxAmountToman:N0} تومان است.");
        }

        var result = await gateway.RequestAsync(request, ct);
        if (!result.Succeeded)
        {
            throw new PortalInvitationException(
                result.FailureReason ?? "پرداخت ناموفق بود. لطفاً دوباره تلاش کنید.");
        }

        if (!result.RequiresRedirect)
        {
            return new GatewayChargeOutcome(
                RequiresRedirect: false,
                RedirectUrl: null,
                SettledAmountToman: result.SimulatedPaidAmountToman ?? request.AmountToman,
                GatewayReference: null);
        }

        if (string.IsNullOrWhiteSpace(result.GatewayReference) || string.IsNullOrWhiteSpace(result.RedirectUrl))
        {
            throw new PortalInvitationException("پاسخ درگاه پرداخت ناقص بود. لطفاً دوباره تلاش کنید.");
        }

        var now = DateTimeOffset.UtcNow;

        // Supersede this subject's own earlier attempts: the customer clicking "pay" again produces
        // a new authority, and the old one must not remain a live settlement path afterwards.
        var superseded = await dbContext.GatewayTransactions
            .Where(t => t.AgencyId == context.AgencyId
                && t.Provider == context.Provider
                && t.Status == GatewayTransactionStatus.Pending
                && t.InvitationId == context.InvitationId
                && t.InstallmentId == context.InstallmentId)
            .ToListAsync(ct);
        foreach (var stale in superseded)
        {
            stale.Status = GatewayTransactionStatus.Expired;
        }

        dbContext.GatewayTransactions.Add(new GatewayTransaction
        {
            AgencyId = context.AgencyId,
            Provider = context.Provider,
            Purpose = context.Purpose,
            GatewayReference = result.GatewayReference,
            MerchantIdUsed = request.MerchantId,
            AmountToman = request.AmountToman,
            Status = GatewayTransactionStatus.Pending,
            InvitationId = context.InvitationId,
            InstallmentId = context.InstallmentId,
            PolicyId = context.PolicyId,
            CustomerId = context.CustomerId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(ChargeWindow),
        });

        // Saved here, before the browser leaves: if the customer pays and the process dies before the
        // callback lands, the reference must already exist for the PSP's retry of the callback to
        // resolve against.
        await dbContext.SaveChangesAsync(ct);

        return new GatewayChargeOutcome(
            RequiresRedirect: true,
            RedirectUrl: result.RedirectUrl,
            SettledAmountToman: null,
            GatewayReference: result.GatewayReference);
    }

    /// <summary>
    /// Phase two. Loads the Pending transaction the callback refers to, verifies it with the PSP, and
    /// returns it MUTATED-BUT-UNSAVED on success so the caller's own finalisation (Payment + audit +
    /// stage change) is committed by the SAME SaveChanges that flips the row to Verified — one
    /// transaction, no window in which a verified charge has produced no Payment.
    ///
    /// A transaction already Verified returns its recorded evidence unchanged: that is a PSP
    /// retrying a callback it is not sure we received, and rule 24 says a repeat must read as
    /// success rather than produce a second settlement.
    /// </summary>
    public async Task<GatewayVerificationOutcome> VerifyAsync(
        PaymentProvider provider, string gatewayReference, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gatewayReference))
        {
            throw new PortalInvitationException("شناسهٔ تراکنش درگاه در بازگشت از پرداخت یافت نشد.");
        }

        var transaction = await dbContext.GatewayTransactions
            .FirstOrDefaultAsync(
                t => t.Provider == provider && t.GatewayReference == gatewayReference, ct)
            ?? throw new PortalInvitationException(
                "تراکنش متناظر با این بازگشت از پرداخت یافت نشد. اگر مبلغی از حساب شما کسر شده، با نمایندگی تماس بگیرید.");

        if (transaction.Status == GatewayTransactionStatus.Verified)
        {
            // A PSP retrying a callback. The requested amount is used because verify only ever
            // succeeds against an exact match, so it is by construction the captured amount too.
            return new GatewayVerificationOutcome(transaction, transaction.AmountToman);
        }

        if (transaction.Status != GatewayTransactionStatus.Pending)
        {
            throw new PortalInvitationException("این پرداخت پیش‌تر ناموفق یا منقضی شده است. لطفاً دوباره تلاش کنید.");
        }

        // Lazily applied expiry, like portal invitations: an Expired row is immutable afterwards.
        if (transaction.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            transaction.Status = GatewayTransactionStatus.Expired;
            await dbContext.SaveChangesAsync(ct);
            throw new PortalInvitationException("مهلت این پرداخت به پایان رسیده است. لطفاً دوباره تلاش کنید.");
        }

        var gateway = Resolve(provider);
        var verification = await gateway.VerifyAsync(
            transaction.GatewayReference, transaction.MerchantIdUsed, transaction.AmountToman, ct);

        if (!verification.Succeeded)
        {
            transaction.Status = GatewayTransactionStatus.Failed;
            transaction.FailureReason = Truncate(verification.FailureReason, 500);
            await dbContext.SaveChangesAsync(ct);
            throw new PortalInvitationException(
                verification.FailureReason ?? "پرداخت تأیید نشد. لطفاً دوباره تلاش کنید.");
        }

        transaction.Status = GatewayTransactionStatus.Verified;
        transaction.VerifiedAtUtc = DateTimeOffset.UtcNow;
        transaction.RefId = Truncate(verification.RefId, 64);
        transaction.PaidCardMask = Truncate(verification.PaidCardMask, 32);
        transaction.BuyerIp = Truncate(verification.BuyerIp, 64);

        // Deliberately NOT saved — see the remarks above. The caller's own SaveChanges commits the
        // Verified flip together with the Payment it produces, so there is no window in which a
        // verified charge exists with no Payment behind it (and a lost SaveChanges leaves the row
        // Pending, which a retried callback simply verifies again).
        return new GatewayVerificationOutcome(
            transaction, verification.PaidAmountToman ?? transaction.AmountToman);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];
}
