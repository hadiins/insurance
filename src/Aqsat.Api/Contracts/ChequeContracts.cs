namespace Aqsat.Api.Contracts;

/// <summary>«فهرست چک‌ها» — one row per cheque from BOTH sources: guarantee cheques
/// (Collateral/ChequeSayadi) and cheques received toward a payment (PaymentCheque). `Source`
/// decides which existing status endpoint updates the row: /api/collateral/{id}/status or
/// /api/payment-cheques/{id}/status.</summary>
public sealed record UnifiedChequeRowDto(
    string Source,
    Guid Id,
    Guid PolicyId,
    string PolicyNumber,
    string CustomerName,
    string? ChequeNumber,
    string? SayadId,
    string BankName,
    decimal Amount,
    DateOnly? DueDate,
    string Status,
    string? PresenterName,
    string? CashBoxName);
