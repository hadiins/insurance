namespace Aqsat.Api.Contracts;

public sealed record AgencyDto(Guid Id, string Code, string Name, string? Province, string? City, string? InsurerName, bool IsActive, int UserCount);

public sealed record CreateAgencyRequest(
    string Code, string Name, string? Province, string? City, string? InsurerName,
    string ManagerFullName, string ManagerMobile, string ManagerPassword, Guid? RoleId);

public sealed record CreateAgencyResultDto(AgencyDto Agency, string ManagerMobile, string RoleName);

public sealed record UpdateAgencyRequest(string Name, string? Province, string? City, string? InsurerName, bool IsActive);

/// <summary>Lifetime totals in one list row, read from the AgencyStatsDaily rollup — this is what
/// makes stat columns sortable across 60k agencies without touching any RLS-scoped table.</summary>
public sealed record AgencyListRowDto(
    Guid Id, string Code, string Name, string? Province, string? City, string? InsurerName,
    bool IsActive, int UserCount,
    int PoliciesTotal, int SmsSentTotal, int InquiryPaymentsTotal, int InquiryCallsTotal,
    decimal InquiryRevenueToman);

public sealed record AgencyListPageDto(int TotalCount, IReadOnlyList<AgencyListRowDto> Rows);

/// <summary>Distinct values the list filters offer — provinces come from the canonical
/// IranProvinces list so an empty province still appears as a selectable option source; cities
/// and insurers are the values actually in use.</summary>
public sealed record AgencyFilterOptionsDto(
    IReadOnlyList<string> Provinces, IReadOnlyList<string> Cities, IReadOnlyList<string> Insurers);

public sealed record AgencyPlatformSummaryDto(
    int TotalAgencies,
    int ActiveAgencies,
    int PoliciesTotal,
    int SmsSentTotal,
    decimal SmsCostToman,
    int InquiryPaymentsTotal,
    decimal InquiryRevenueToman,
    int InquiryCallsTotal,
    IReadOnlyList<AgencyProvinceStatDto> ByProvince);

public sealed record AgencyProvinceStatDto(
    string? Province,
    int AgencyCount,
    int PoliciesTotal,
    int SmsSentTotal,
    int InquiryPaymentsTotal,
    decimal InquiryRevenueToman);

public sealed record AgencyMonthlyTrendDto(int Year, int Month, int Policies, int Sms, int InquiryPayments, int InquiryCalls, decimal InquiryRevenueToman);

/// <summary>پروندهٔ نمایندگی — identity plus live totals computed inside the agency's RLS scope,
/// so the numbers are exact even mid-day between rollup runs.</summary>
public sealed record AgencyProfileDto(
    Guid Id, string Code, string Name, string? Province, string? City, string? InsurerName,
    bool IsActive, string? AgencyCode, int UserCount,
    int PoliciesIssuedTotal, int PoliciesThisMonth,
    int SmsSentTotal, decimal SmsCostToman,
    int InquiryPaymentsTotal, decimal InquiryRevenueToman,
    int InquiryCallsTotal, decimal InquiryCallCostToman,
    IReadOnlyList<AgencyMonthlyTrendDto> MonthlyTrend);
