namespace Aqsat.Api.Contracts;

public sealed record CreateRenewalWatchRequest(
    Guid? CustomerId,
    string? ProspectName,
    string? ProspectMobile,
    Guid InsuranceLineId,
    string? CurrentInsurer,
    DateOnly CurrentExpiryDate,
    int NotifyDaysBefore,
    Guid? MarketerId);

public sealed record ConvertRenewalWatchRequest(Guid PolicyId);

public sealed record RenewalWatchDto(
    Guid Id,
    Guid? CustomerId,
    string? CustomerFullName,
    string? ProspectName,
    string? ProspectMobile,
    Guid InsuranceLineId,
    string InsuranceLineNameFa,
    string? CurrentInsurer,
    DateOnly CurrentExpiryDate,
    int NotifyDaysBefore,
    Guid? MarketerId,
    string? MarketerFullName,
    string Status,
    Guid? PolicyId);
