namespace Aqsat.Api.Contracts;

public sealed record OpenInstallmentRow(Guid Id, int SeqNo, DateOnly DueDate, decimal Amount, decimal Balance, string Status);

/// <summary>Backs «ثبت دریافت» — the single-policy state a receipt-recording page needs: is it
/// scheduled yet, has the down payment already been received, which installments are still open,
/// and (for non-installment policies) has the one full payment already landed.</summary>
public sealed record PolicyReceiptStatusDto(
    Guid PolicyId, string PolicyNumber, string CustomerFullName, bool IsInstallment, bool IsScheduled,
    decimal TotalReceivable, decimal DownPayment, bool DownPaymentReceived,
    IReadOnlyList<OpenInstallmentRow> OpenInstallments, bool IsFullyPaid);
