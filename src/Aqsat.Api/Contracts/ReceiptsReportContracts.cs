namespace Aqsat.Api.Contracts;

public sealed record ReceiptRow(
    Guid PaymentId, DateOnly PaidOn, string PolicyNumber, string CustomerFullName, decimal Amount,
    string MethodType, string? ReferenceNo, string? CashBoxName, string? BankAccountLabel,
    string? ChequeNumber, string? ChequeStatus);

public sealed record ReceiptsByMethodRow(string MethodType, int Count, decimal Amount);

public sealed record ReceiptsByChequeStatusRow(string Status, int Count, decimal Amount);

public sealed record ReceiptsReportDto(
    decimal TotalAmount,
    int Count,
    IReadOnlyList<ReceiptsByMethodRow> ByMethod,
    IReadOnlyList<ReceiptsByChequeStatusRow> ByChequeStatus,
    IReadOnlyList<ReceiptRow> Rows);
