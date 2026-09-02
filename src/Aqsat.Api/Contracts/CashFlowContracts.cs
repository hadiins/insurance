using Aqsat.Domain.Enums;

namespace Aqsat.Api.Contracts;

/// <summary>One cash box / bank account with its live balance — "P&amp;L is not accounting"
/// (CLAUDE.md): no ledger, just opening balance + arithmetic over the flows already recorded
/// (Payments in; Expenses, InsurerRemittances and CommissionPayouts out; FundTransfers both ways).</summary>
public sealed record FundBalanceDto(
    Guid Id, string Label, bool IsBankAccount, bool IsActive,
    decimal OpeningBalance, decimal TotalIn, decimal TotalOut, decimal Balance);

public sealed record CashFlowBalancesDto(IReadOnlyList<FundBalanceDto> CashBoxes, IReadOnlyList<FundBalanceDto> BankAccounts);

/// <summary>Exactly one "from" side and one "to" side must be set — cash box or bank account for
/// each, but never both of a side and never neither.</summary>
public sealed record CreateFundTransferRequest(
    DateOnly Date, decimal Amount,
    Guid? FromCashBoxId, Guid? FromBankAccountId, Guid? ToCashBoxId, Guid? ToBankAccountId,
    string? Note = null);

public sealed record FundTransferDto(
    Guid Id, DateOnly Date, decimal Amount,
    string? FromLabel, string? ToLabel, string? Note);

/// <summary>One row of the unified movement view for a box/account — every flow the balance is
/// built from, newest first. Kind is a Persian display string composed at write time of the query,
/// in the codebase's DescribeChange spirit.</summary>
public sealed record FundMovementDto(
    DateOnly Date, string Kind, string Label, decimal? AmountIn, decimal? AmountOut, string? ReferenceNo);

public sealed record FundMovementsDto(decimal TotalIn, decimal TotalOut, IReadOnlyList<FundMovementDto> Rows);
