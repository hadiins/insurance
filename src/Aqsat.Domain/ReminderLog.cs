using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// docs/PHASE-1-SPEC.md §3.7 — the per-(installment, offset) idempotency record that makes
/// "if already sent (installmentId, offset): skip" possible, and the delivery report. Exactly one
/// of InstallmentId/RenewalWatchId is set.
/// </summary>
public class ReminderLog : AgencyOwnedEntity
{
    public Guid? InstallmentId { get; set; }
    public Installment? Installment { get; set; }

    public Guid? RenewalWatchId { get; set; }
    public RenewalWatch? RenewalWatch { get; set; }

    public ReminderRecipientType RecipientType { get; set; }
    public string Mobile { get; set; } = default!;

    /// <summary>Days before due date (or renewal expiry) this reminder fired at — part of the
    /// dedupe key, never a display-only field.</summary>
    public int OffsetDays { get; set; }

    public string TemplateKey { get; set; } = default!;
    public ReminderChannel Channel { get; set; }
    public ReminderSendStatus Status { get; set; }
    public string? ProviderMessageId { get; set; }
    public DateTimeOffset SentAt { get; set; }
}
