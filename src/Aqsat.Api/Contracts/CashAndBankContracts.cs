namespace Aqsat.Api.Contracts;

public sealed record CashBoxDto(Guid Id, string Name, bool IsActive);

public sealed record CreateCashBoxRequest(string Name);

public sealed record UpdateCashBoxRequest(string Name, bool IsActive);

public sealed record BankAccountDto(Guid Id, string BankName, string AccountNumber, string? AccountHolderName, bool IsActive);

public sealed record CreateBankAccountRequest(string BankName, string AccountNumber, string? AccountHolderName);

public sealed record UpdateBankAccountRequest(string BankName, string AccountNumber, string? AccountHolderName, bool IsActive);
