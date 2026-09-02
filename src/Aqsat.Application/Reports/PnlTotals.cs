namespace Aqsat.Application.Reports;

public enum PnlBasis
{
    /// <summary>Recognise income at issuance — docs/PHASE-1-SPEC.md §3.6.</summary>
    Accrual,

    /// <summary>Recognise income at collection, prorated by how much of each policy's
    /// TotalReceivable was actually paid in the period.</summary>
    Cash,
}

/// <summary>
/// docs/PHASE-1-SPEC.md §3.6: درآمد (AgencyCommission + ServiceFee) − هزینه (marketer commission +
/// default write-off + operating expense) = سود خالص. The inputs are aggregated by the caller
/// (real DB queries); this is only the arithmetic guard — income/expense/net must never silently
/// drift apart from their parts, which is exactly what "hand-compute one month and match it to the
/// rial" checks. OperatingExpense (stage 7/7) is the agency's own real costs (Expense), never a
/// derived/estimated figure like default write-off. MarketerCommissionPaid is informational only
/// (cash actually handed over via CommissionPayout in the period) — the expense itself is already
/// recognized at EligibleAt, so it must NOT enter TotalExpense or the two would double-count.
/// </summary>
public readonly record struct PnlTotals(
    decimal AgencyCommissionIncome, decimal ServiceFeeIncome, decimal MarketerCommissionExpense, decimal DefaultWriteOffExpense,
    decimal OperatingExpense = 0, decimal MarketerCommissionPaid = 0)
{
    public decimal TotalIncome => AgencyCommissionIncome + ServiceFeeIncome;
    public decimal TotalExpense => MarketerCommissionExpense + DefaultWriteOffExpense + OperatingExpense;
    public decimal NetProfit => TotalIncome - TotalExpense;

    public static PnlTotals operator +(PnlTotals a, PnlTotals b) => new(
        a.AgencyCommissionIncome + b.AgencyCommissionIncome,
        a.ServiceFeeIncome + b.ServiceFeeIncome,
        a.MarketerCommissionExpense + b.MarketerCommissionExpense,
        a.DefaultWriteOffExpense + b.DefaultWriteOffExpense,
        a.OperatingExpense + b.OperatingExpense,
        a.MarketerCommissionPaid + b.MarketerCommissionPaid);
}
