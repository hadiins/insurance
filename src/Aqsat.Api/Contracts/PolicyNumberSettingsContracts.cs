namespace Aqsat.Api.Contracts;

public sealed record InsuranceLineCodeDto(Guid Id, Guid InsuranceLineId, string InsuranceLineNameFa, string Code, bool IsActive);

public sealed record CreateInsuranceLineCodeRequest(Guid InsuranceLineId, string Code);

public sealed record UpdateInsuranceLineCodeRequest(string Code, bool IsActive);

public sealed record PolicyNumberFormatDto(
    Guid Id, string InsurerName, string Pattern, string Separator,
    int LineCodeLength, int AgencyCodeLength, int YearDigits, int SerialLength, bool IsStrict);

public sealed record UpdatePolicyNumberFormatRequest(
    string Separator, int LineCodeLength, int AgencyCodeLength, int YearDigits, int SerialLength, bool IsStrict);
