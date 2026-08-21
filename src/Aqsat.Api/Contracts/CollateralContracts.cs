namespace Aqsat.Api.Contracts;

public sealed record CreateCollateralRequest(
    Guid PolicyId, string Type, string? SayadId, string? BankName, decimal Amount, DateOnly? DueDate);

public sealed record UpdateCollateralStatusRequest(string Status);

public sealed record CollateralDto(
    Guid Id, Guid PolicyId, string PolicyNumber, string CustomerFullName, string Type,
    string? SayadId, string? BankName, decimal Amount, DateOnly? DueDate, string Status,
    string? ColorCode, DateTimeOffset? CheckedAt);
