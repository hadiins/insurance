namespace Aqsat.Api.Contracts;

public sealed record CashBoxDto(Guid Id, string Name, bool IsActive, decimal OpeningBalance = 0);

public sealed record CreateCashBoxRequest(string Name, decimal OpeningBalance = 0);

public sealed record UpdateCashBoxRequest(string Name, bool IsActive, decimal OpeningBalance = 0);

public sealed record BankAccountDto(Guid Id, string BankName, string AccountNumber, string? AccountHolderName, bool IsActive, decimal OpeningBalance = 0);

public sealed record CreateBankAccountRequest(string BankName, string AccountNumber, string? AccountHolderName, decimal OpeningBalance = 0);

public sealed record UpdateBankAccountRequest(string BankName, string AccountNumber, string? AccountHolderName, bool IsActive, decimal OpeningBalance = 0);

public sealed record BankDto(Guid Id, string Name, bool IsActive);

public sealed record CreateBankRequest(string Name);

public sealed record UpdateBankRequest(string Name, bool IsActive);
