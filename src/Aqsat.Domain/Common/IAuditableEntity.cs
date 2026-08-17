using Aqsat.Domain.Enums;

namespace Aqsat.Domain.Common;

/// <summary>
/// Entities whose Added/Modified changes must produce an AuditEntry (CLAUDE.md rules 27-31),
/// written by AppDbContext.SaveChangesAsync inside the same transaction as the change itself so no
/// developer can forget it. PolicyId is always populated per rule 28, even when the audit subject
/// is technically an installment or a cheque, not the policy itself.
///
/// Entities that cannot resolve PolicyId from their own properties (e.g. Payment, which only knows
/// its Customer and an installment hint) do not implement this — a future task's write service
/// resolves PolicyId explicitly and adds its own AuditEntry (see AuditAction.PaymentRecorded /
/// LockForceReleased) rather than relying on this generic path, to avoid double-auditing the same
/// write.
/// </summary>
public interface IAuditableEntity
{
    Guid PolicyId { get; }

    /// <summary>
    /// Short Persian phrase describing this specific change, composed at write time (rule 31) —
    /// e.g. "ثبت بیمه‌نامهٔ ..." or "ویرایش قسط شمارهٔ ۲" — not reconstructed later from raw column
    /// values when the audit timeline renders.
    /// </summary>
    string DescribeChange(AuditAction action);
}
