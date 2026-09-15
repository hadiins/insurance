using Aqsat.Application.Payments;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Portal;

public sealed record PaymentLinkInfo(
    string CustomerDisplayName, string AgencyName, IReadOnlyList<PaymentLinkInstallmentDto> Installments);

public sealed record PaymentLinkInstallmentDto(
    Guid Id, int SeqNo, string PolicyNumber, DateOnly DueDate, decimal BalanceToman, bool IsOverdue);

public sealed record PortalInstallmentPaymentResult(
    decimal PaidAmountToman, DateTimeOffset PaidAtUtc, string PolicyNumber, int SeqNo);

/// <summary>
/// The installment-payment half of the customer portal: a long-lived per-customer link
/// ({Portal:PublicBaseUrl}/pay/{token}) the SMS reminder carries, so the customer can pay any
/// open installment online through the AGENCY's own gateway. The recorded Payment /
/// PaymentAllocation / installment status / commission-flip state mirrors what
/// PaymentsController.RecordPaymentAsync produces for an agent-recorded payment — the parity
/// test in InstallmentPaymentLinkEndpointTests locks the two paths together. Operator actions
/// (revoke, link status) run inside the caller's RLS scope; the public token flow resolves the
/// agency through the RLS-exempt PaymentLinkTokenIndex, then works scoped like any other read.
/// </summary>
public sealed class InstallmentPaymentLinkService(
    AppDbContext dbContext,
    IEnumerable<IPaymentGateway> gateways)
{
    /// <summary>Returns the customer's active link, creating it if none exists — the reminder job
    /// calls this at send time so a link always exists before the SMS goes out. Every call
    /// refreshes the rolling expiry (now + PaymentLinkTtlDays) and stamps LastSentAtUtc. Saves
    /// immediately: the reminder loop's own DbUpdateException handler detaches everything pending,
    /// so a half-saved link creation must never ride along in that batch.</summary>
    public async Task<CustomerPaymentLink> EnsureLinkAsync(
        Guid customerId, Guid createdByUserId, CancellationToken ct = default)
    {
        var agencyId = Aqsat.Infrastructure.Persistence.AgencyContext.Current
            ?? throw new PortalInvitationException("دامنهٔ نمایندگی نامعتبر است.");

        var settings = await dbContext.OrgSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == agencyId, ct);
        var ttlDays = settings?.PaymentLinkTtlDays ?? new OrgSettings().PaymentLinkTtlDays;
        var now = DateTimeOffset.UtcNow;

        var link = await dbContext.CustomerPaymentLinks
            .FirstOrDefaultAsync(
                l => l.CustomerId == customerId && l.Status == PaymentLinkStatus.Active && !l.IsDeleted, ct);
        if (link is not null)
        {
            link.ExpiresAtUtc = now.AddDays(ttlDays);
            link.LastSentAtUtc = now;
            await dbContext.SaveChangesAsync(ct);
            return link;
        }

        link = new CustomerPaymentLink
        {
            // Client-side key — the RLS-exempt PaymentLinkTokenIndex row below references it by
            // Id in this same SaveChanges, and there is no FK relationship for EF's temp-value
            // fixup to ride on: a database-generated key would leave the index row pointing at
            // an empty Guid (same reasoning as the Payment key in PayInstallmentAsync).
            Id = SequentialGuidGenerator.Next(),
            AgencyId = agencyId,
            CustomerId = customerId,
            Token = GenerateToken(),
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(ttlDays),
            LastSentAtUtc = now,
            Status = PaymentLinkStatus.Active,
        };
        dbContext.CustomerPaymentLinks.Add(link);
        // The RLS-exempt token→agency row, written in the same transaction as the link — the
        // anonymous pay page resolves the agency from this, never from the RLS-scoped table.
        dbContext.PaymentLinkTokenIndex.Add(new PaymentLinkTokenIndex
        {
            AgencyId = agencyId,
            Token = link.Token,
            LinkId = link.Id,
        });

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two reminder runs raced the one-active-link unique index — the other run's link won.
            // Re-fetch it TRACKED: an AsNoTracking entity mutated and saved is a silent no-op
            // (SaveChanges sees nothing changed), which would return the caller a link whose
            // expiry/LastSentAt never actually refreshed.
            DetachPendingChanges(dbContext.ChangeTracker);
            link = await dbContext.CustomerPaymentLinks
                .FirstAsync(
                    l => l.CustomerId == customerId && l.Status == PaymentLinkStatus.Active && !l.IsDeleted, ct);
            link.ExpiresAtUtc = now.AddDays(ttlDays);
            link.LastSentAtUtc = now;
            await dbContext.SaveChangesAsync(ct);
        }

        return link;
    }

    /// <summary>Everything the public /pay/{token} page renders: the customer's display name, the
    /// agency's name, and every open installment across all their policies (the same customer-wide
    /// unsettled set an agent's receipt allocates over). Anonymous — the token is the credential.</summary>
    public async Task<PaymentLinkInfo> GetOpenInstallmentsAsync(string token, CancellationToken ct = default)
    {
        return await RunWithLinkScopeAsync(token, ct, async (link, agencyId) =>
        {
            await ApplyLazyExpiryAsync(link, ct);
            EnsureUsable(link);

            var customerName = await dbContext.Customers.AsNoTracking()
                .Where(c => c.Id == link.CustomerId)
                .Select(c => c.FullName)
                .FirstAsync(ct);
            var agencyName = await dbContext.Organizations.AsNoTracking()
                .Where(o => o.Id == agencyId)
                .Select(o => o.Name)
                .FirstAsync(ct);

            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.LocalDateTime);
            var installments = await dbContext.Installments.AsNoTracking()
                .Where(i => i.Policy.CustomerId == link.CustomerId && i.Status != InstallmentStatus.Settled)
                .OrderBy(i => i.DueDate)
                .Select(i => new PaymentLinkInstallmentDto(
                    i.Id, i.SeqNo, i.Policy.PolicyNumber, i.DueDate, i.Amount - i.PaidAmount, i.DueDate < today))
                .ToListAsync(ct);

            return new PaymentLinkInfo(customerName, agencyName, installments);
        });
    }

    /// <summary>Pays ONE installment at its full remaining balance through the agency's own
    /// gateway — no custom amounts, so no overpayment from this path (an under-capture by a real
    /// PSP still lands correctly as a partial payment). Records the Payment idempotently exactly
    /// like an agent receipt (rule 24) and flips the installment's commission slices Payable on
    /// full settlement, so an online and a manual receipt land in the same state.</summary>
    public async Task<PortalInstallmentPaymentResult> PayInstallmentAsync(
        string token, Guid installmentId, string? customerIp, CancellationToken ct = default)
    {
        return await RunWithLinkScopeAsync(token, ct, async (link, agencyId) =>
        {
            await ApplyLazyExpiryAsync(link, ct);
            EnsureUsable(link);

            // RLS scopes this read to the link's agency (rule 10); the ownership check below is the
            // rule-11 belt — an in-scope installment of a DIFFERENT customer must not be payable.
            var installment = await dbContext.Installments
                .Include(i => i.Policy)
                .FirstOrDefaultAsync(i => i.Id == installmentId, ct)
                ?? throw new PortalInvitationException("قسط یافت نشد.");
            if (installment.Policy.CustomerId != link.CustomerId)
            {
                throw new PortalInvitationException("قسط یافت نشد.");
            }
            var amount = installment.Balance;
            var paidOn = DateOnly.FromDateTime(DateTimeOffset.UtcNow.LocalDateTime);

            // Idempotency pre-check (rule 24) — BEFORE the settled check: a retry after a
            // successful payment finds the installment already settled, and that retry must
            // return the original result, not "already settled". Matched on the portal method,
            // because the balance is now zero and cannot be compared against the original amount.
            var existing = await dbContext.Payments.AsNoTracking().FirstOrDefaultAsync(
                p => p.InstallmentIdHint == installment.Id
                    && p.PaidOn == paidOn
                    && p.Method == WellKnownPaymentMethods.OnlineInstallment, ct);
            if (existing is not null)
            {
                return new PortalInstallmentPaymentResult(
                    existing.Amount, DateTimeOffset.UtcNow, installment.Policy.PolicyNumber, installment.SeqNo);
            }

            if (installment.Status == InstallmentStatus.Settled || installment.Balance <= 0)
            {
                throw new PortalInvitationException("این قسط قبلاً به‌طور کامل تسویه شده است.");
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

            // Idempotent per (link, installment): one charge slot per installment per link, so
            // paying several installments through one link and retrying a timed-out charge both
            // behave — the gateway deduplicates on this token.
            var result = await gateway.ChargeAsync(
                $"{link.Token}:{installment.Id:N}", merchantId ?? string.Empty, amount,
                $"پرداخت قسط شمارهٔ {installment.SeqNo} بیمه‌نامهٔ {installment.Policy.PolicyNumber}",
                string.Empty, ct);
            if (!result.Succeeded)
            {
                throw new PortalInvitationException(result.FailureReason ?? "پرداخت ناموفق بود. لطفاً دوباره تلاش کنید.");
            }

            // A gateway that reports no amount falls back to the asked amount, but a NON-POSITIVE
            // reported amount is a protocol violation, not an under-capture — letting it through
            // would record a zero/negative Payment and corrupt the balance math.
            var settledAmount = result.PaidAmountToman ?? amount;
            if (settledAmount <= 0)
            {
                throw new PortalInvitationException("مبلغ پرداخت دریافتی از درگاه نامعتبر است. لطفاً با نمایندگی تماس بگیرید.");
            }
            var occurredAt = DateTimeOffset.UtcNow;
            var payment = new Payment
            {
                // Client-side key — the audit row below references it by Id in this same
                // SaveChanges; a database-generated key would leave the audit pointing at EF's
                // empty placeholder.
                Id = SequentialGuidGenerator.Next(),
                AgencyId = agencyId,
                CustomerId = link.CustomerId,
                InstallmentIdHint = installment.Id,
                Amount = settledAmount,
                PaidOn = paidOn,
                Method = WellKnownPaymentMethods.OnlineInstallment,
                ReferenceNo = $"PORTAL-{link.Token[..12]}",
                MethodType = PaymentMethod.Online,
                RecordedByUserId = link.CreatedByUserId,
            };
            dbContext.Payments.Add(payment);

            // A real PSP may capture slightly off the asked amount: apply at most the balance, the
            // remainder stays unallocated (customer credit, editable by the agent — rule 22).
            var applied = Math.Min(settledAmount, installment.Balance);
            installment.PaidAmount += applied;
            installment.Status = RecomputeStatus(installment.PaidAmount, installment.Amount);

            // Task 13 — full settlement flips this installment's commission slice to payable,
            // mirroring RecordPaymentAsync exactly.
            if (installment.Status == InstallmentStatus.Settled)
            {
                var commissionEntry = await dbContext.CommissionEntries
                    .FirstOrDefaultAsync(c => c.InstallmentId == installment.Id, ct);
                if (commissionEntry is { Status: CommissionStatus.Pending })
                {
                    commissionEntry.Status = CommissionStatus.Payable;
                    commissionEntry.EligibleAt = occurredAt;
                }

                var agencyCommissionEntry = await dbContext.AgencyCommissionEntries
                    .FirstOrDefaultAsync(c => c.InstallmentId == installment.Id, ct);
                if (agencyCommissionEntry is { Status: CommissionStatus.Pending })
                {
                    agencyCommissionEntry.Status = CommissionStatus.Payable;
                    agencyCommissionEntry.EligibleAt = occurredAt;
                }
            }

            dbContext.PaymentAllocations.Add(new PaymentAllocation
            {
                AgencyId = agencyId,
                Payment = payment,
                InstallmentId = installment.Id,
                Amount = applied,
            });

            dbContext.AuditEntries.Add(new AuditEntry
            {
                AgencyId = agencyId,
                UserId = link.CreatedByUserId,
                UserDisplayName = "پورتال مشتری",
                EntityType = nameof(Payment),
                EntityId = payment.Id,
                PolicyId = installment.PolicyId,
                Action = AuditAction.PaymentRecorded,
                Description = $"پرداخت آنلاین قسط شمارهٔ {installment.SeqNo} بیمه‌نامهٔ {installment.Policy.PolicyNumber} ({applied:N0} تومان) از طریق لینک پیامکی",
                OccurredAt = occurredAt,
                IpAddress = customerIp,
            });

            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent duplicate submission raced the pre-check above — the unique index
                // caught it at the database. Treat it as success, per rule 24, not as a failure.
                DetachPendingChanges(dbContext.ChangeTracker);
                var raced = await dbContext.Payments.AsNoTracking().FirstOrDefaultAsync(
                    p => p.InstallmentIdHint == installment.Id
                        && p.PaidOn == paidOn
                        && p.Method == WellKnownPaymentMethods.OnlineInstallment, ct);
                if (raced is not null)
                {
                    return new PortalInstallmentPaymentResult(
                        raced.Amount, DateTimeOffset.UtcNow, installment.Policy.PolicyNumber, installment.SeqNo);
                }

                throw new PortalInvitationException("خطای پایگاه‌داده هنگام ثبت پرداخت.");
            }

            return new PortalInstallmentPaymentResult(
                settledAmount, occurredAt, installment.Policy.PolicyNumber, installment.SeqNo);
        });
    }

    /// <summary>The customer's active link, for the agency-side status display — null when the
    /// customer has none. Runs in the caller's RLS scope.</summary>
    public async Task<CustomerPaymentLink?> GetActiveLinkAsync(Guid customerId, CancellationToken ct = default) =>
        await dbContext.CustomerPaymentLinks.AsNoTracking()
            .FirstOrDefaultAsync(
                l => l.CustomerId == customerId && l.Status == PaymentLinkStatus.Active && !l.IsDeleted, ct);

    /// <summary>Revokes the customer's active payment link (operator action). Returns false when
    /// there was nothing active to revoke. Re-sending a reminder afterwards creates a fresh link
    /// with a new token — the revoked token never resolves again.</summary>
    public async Task<bool> RevokeAsync(
        Guid customerId, Guid currentUserId, string currentUserDisplayName, CancellationToken ct = default)
    {
        var link = await dbContext.CustomerPaymentLinks
            .FirstOrDefaultAsync(
                l => l.CustomerId == customerId && l.Status == PaymentLinkStatus.Active && !l.IsDeleted, ct);
        if (link is null)
        {
            return false;
        }

        link.Status = PaymentLinkStatus.Revoked;

        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = link.AgencyId,
            UserId = currentUserId,
            UserDisplayName = currentUserDisplayName,
            EntityType = nameof(CustomerPaymentLink),
            EntityId = link.Id,
            PolicyId = Guid.Empty,
            Action = AuditAction.PaymentLinkRevoked,
            Description = $"لغو لینک پرداخت آنلاین اقساط (توکن {link.Token[..12]}…)",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync(ct);
        return true;
    }

    private static void EnsureUsable(CustomerPaymentLink link)
    {
        if (link.Status == PaymentLinkStatus.Revoked)
        {
            throw new PortalInvitationException(
                "این لینک توسط نمایندگی لغو شده است. لطفاً با نمایندگی تماس بگیرید.");
        }
        if (link.Status == PaymentLinkStatus.Expired)
        {
            throw new PortalInvitationException(
                "اعتبار این لینک به پایان رسیده است. لطفاً از نمایندگی لینک جدید دریافت کنید.");
        }
    }

    private async Task ApplyLazyExpiryAsync(CustomerPaymentLink link, CancellationToken ct)
    {
        if (link.Status == PaymentLinkStatus.Active && link.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            link.Status = PaymentLinkStatus.Expired;
            await dbContext.SaveChangesAsync(ct);
        }
    }

    /// <summary>Runs work inside the link's agency scope: resolves the agency via the RLS-exempt
    /// PaymentLinkTokenIndex (the one token-keyed lookup an anonymous visitor gets), then reopens
    /// the connection so the session-context interceptor stamps the resolved agency, and keeps
    /// both the ambient scope and the open connection alive for the whole unit of work — every
    /// read, expiry flip and SaveChanges inside stays RLS-scoped to that agency.</summary>
    private async Task<TResult> RunWithLinkScopeAsync<TResult>(
        string token, CancellationToken ct, Func<CustomerPaymentLink, Guid, Task<TResult>> work)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 20 or > 43)
        {
            throw new PortalInvitationException("لینک نامعتبر است.");
        }

        var agencyId = await dbContext.PaymentLinkTokenIndex.AsNoTracking()
            .Where(t => t.Token == token)
            .Select(t => t.AgencyId)
            .FirstOrDefaultAsync(ct);

        if (agencyId == Guid.Empty)
        {
            throw new PortalInvitationException("لینک یافت نشد یا اعتبار آن به پایان رسیده است.");
        }

        using var agencyScope = Aqsat.Infrastructure.Persistence.AgencyContext.BeginScope(agencyId);
        await dbContext.Database.OpenConnectionAsync(ct);
        try
        {
            var link = await dbContext.CustomerPaymentLinks
                .FirstOrDefaultAsync(l => l.Token == token, ct)
                ?? throw new PortalInvitationException("لینک یافت نشد یا اعتبار آن به پایان رسیده است.");
            return await work(link, agencyId);
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static InstallmentStatus RecomputeStatus(decimal paidAmount, decimal amount) =>
        paidAmount <= 0 ? InstallmentStatus.Unpaid : paidAmount >= amount ? InstallmentStatus.Settled : InstallmentStatus.Partial;

    private static void DetachPendingChanges(Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker changeTracker)
    {
        foreach (var entry in changeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static string GenerateToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
