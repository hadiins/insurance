using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// The 80% problem: most installment policies are identified by counterparty contract name, not
/// the literal «اقساطی» label. Matching is prefix/contains on ContractNamePattern, configured by
/// the agency — never grep for «اقساطی» in the import pipeline (CLAUDE.md).
/// </summary>
public class ContractTemplate : AgencyOwnedEntity
{
    public string ContractNamePattern { get; set; } = default!;
    public bool IsInstallment { get; set; }
    public int DefaultInstallmentCount { get; set; }
    public decimal? SuggestedDownPaymentPercent { get; set; }
}
