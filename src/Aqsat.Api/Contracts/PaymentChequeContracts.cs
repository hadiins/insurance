namespace Aqsat.Api.Contracts;

public sealed record PaymentChequeDto(
    Guid Id, Guid PolicyId, string PolicyNumber, string CustomerFullName,
    string ChequeNumber, string BankName, DateOnly DueDate, string PresenterName,
    string CashBoxName, decimal Amount, string Status);

public sealed record UpdatePaymentChequeStatusRequest(string Status);
