namespace Aqsat.Api.Contracts;

public sealed record ContractTemplateDto(
    Guid Id,
    string ContractNamePattern,
    bool IsInstallment,
    int DefaultInstallmentCount,
    decimal? SuggestedDownPaymentPercent);

public sealed record SaveContractTemplateRequest(
    string ContractNamePattern,
    bool IsInstallment,
    int DefaultInstallmentCount,
    decimal? SuggestedDownPaymentPercent);
