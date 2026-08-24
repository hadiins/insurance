namespace Aqsat.Api.Contracts;

public sealed record AgencyCommissionRateDto(
    Guid Id, Guid InsuranceLineId, string InsuranceLineNameFa, decimal RatePercent, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public sealed record SetAgencyCommissionRateRequest(Guid InsuranceLineId, decimal RatePercent, DateOnly EffectiveFrom);
