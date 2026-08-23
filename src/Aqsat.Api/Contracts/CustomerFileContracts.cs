namespace Aqsat.Api.Contracts;

public sealed record CustomerListItemDto(Guid Id, string FullName, string? Mobile, int PolicyCount);

public sealed record CustomerFileDto(
    Guid CustomerId,
    string FullName,
    string? Mobile,
    string? NationalIdMasked,
    decimal AggregateBalance,
    IReadOnlyList<CustomerPolicySummaryDto> Policies,
    IReadOnlyList<CustomerPaymentDto> Payments,
    IReadOnlyList<CustomerCollateralDto> Collateral,
    IReadOnlyList<TimelineEntryDto> Timeline);

public sealed record CustomerPolicySummaryDto(
    Guid PolicyId, string PolicyNumber, string InsuranceLineNameFa, string Status, decimal TotalReceivable, decimal Balance);

public sealed record CustomerPaymentDto(
    Guid Id, decimal Amount, DateOnly PaidOn, string Method, string? ReferenceNo, IReadOnlyList<string> AllocatedTo);

public sealed record CustomerCollateralDto(
    Guid Id, string PolicyNumber, string Type, decimal Amount, DateOnly? DueDate, string Status);

/// <summary>The standalone "ثبت پرداخت مستقل" flow — recording a payment without coming from the
/// countdown dashboard first. Only installments still owed (Unpaid/Partial) across every one of the
/// customer's policies, oldest due date first, matching the same allocation order Task 10's payment
/// recording already applies by default.</summary>
public sealed record OpenInstallmentDto(
    Guid InstallmentId, string PolicyNumber, string InsuranceLineNameFa, int SeqNo, DateOnly DueDate, decimal Balance, string Status);

/// <summary>مشتریان پرریسک — risk is read directly off existing data (overdue installments +
/// bounced cheques), never a separate score anyone has to maintain: currently-overdue installments
/// past their settlement deadline, and any cheque that has ever bounced.</summary>
public sealed record HighRiskCustomerDto(
    Guid CustomerId, string FullName, string? Mobile, int OverdueInstallmentCount, int MaxDaysOverdue, int BouncedChequeCount);
