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
/// default write-off) = سود خالص. The four inputs are aggregated by the caller (real DB queries);
/// this is only the arithmetic guard — income/expense/net must never silently drift apart from their
/// four parts, which is exactly what "hand-compute one month and match it to the rial" checks.
/// </summary>
public readonly record struct PnlTotals(
    decimal AgencyCommissionIncome, decimal ServiceFeeIncome, decimal MarketerCommissionExpense, decimal DefaultWriteOffExpense)
{
    public decimal TotalIncome => AgencyCommissionIncome + ServiceFeeIncome;
    public decimal TotalExpense => MarketerCommissionExpense + DefaultWriteOffExpense;
    public decimal NetProfit => TotalIncome - TotalExpense;

    public static PnlTotals operator +(PnlTotals a, PnlTotals b) => new(
        a.AgencyCommissionIncome + b.AgencyCommissionIncome,
        a.ServiceFeeIncome + b.ServiceFeeIncome,
        a.MarketerCommissionExpense + b.MarketerCommissionExpense,
        a.DefaultWriteOffExpense + b.DefaultWriteOffExpense);
}
