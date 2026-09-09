using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// The structured credit report retrieved from api.ir (UnpaidCheque + ActiveLoans) the moment an
/// inquiry fee is paid — either in a policy-verification chain (reviewed before approving the
/// installment policy, owner decision 2026-09-01) or via a standalone portal link issued from the
/// customer file (PolicyId=null). A standalone report is reused by the issuance wizard within the
/// 30-day cache window (CLAUDE.md rule 26) by copying it onto the new policy.
///
/// Structured numbers only, never free text about a person (CLAUDE.md rule 8): api.ir also
/// returns the person's name in ActiveLoansRes and it is deliberately NOT stored here. Every field
/// is nullable because a sandboxed or failed inquiry has no real answer — RawSuccess=false marks
/// that, and the representative sees it instead of silently-zero figures. Amounts are stored in
/// TOMAN (rule 19); api.ir returns rials and the retrieval service divides by 10.
/// </summary>
public class CreditReport : AgencyOwnedEntity
{
    /// <summary>Null for a standalone report (customer-file portal link); the wizard reuse path
    /// copies such reports onto a policy with the original RetrievedAtUtc preserved.</summary>
    public Guid? PolicyId { get; set; }
    public Policy? Policy { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = default!;

    public int? ChequeCount { get; set; }
    public decimal? ChequeSumAmountToman { get; set; }
    public decimal? ChequeSumBouncedAmountToman { get; set; }

    public int? ActiveLoansCount { get; set; }
    public decimal? LoanTotalAmountToman { get; set; }
    public decimal? LoanDebtTotalAmountToman { get; set; }
    public decimal? LoanPastExpiredTotalAmountToman { get; set; }
    public decimal? LoanDeferredTotalAmountToman { get; set; }
    public decimal? LoanSuspiciousTotalAmountToman { get; set; }
    public decimal? LoanDishonoredAmountToman { get; set; }

    /// <summary>True only when BOTH inquiries returned real data — false means sandboxed or
    /// failed, and the representative UI must say so rather than render zeros as facts.</summary>
    public bool RawSuccess { get; set; }

    public DateTimeOffset RetrievedAtUtc { get; set; }
}
