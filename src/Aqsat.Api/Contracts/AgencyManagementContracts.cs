namespace Aqsat.Api.Contracts;

public sealed record AgencyDto(Guid Id, string Code, string Name, string? City, string? InsurerName, bool IsActive, int UserCount);

public sealed record CreateAgencyRequest(
    string Code, string Name, string? City, string? InsurerName,
    string ManagerFullName, string ManagerMobile, string ManagerPassword, Guid? RoleId);

public sealed record CreateAgencyResultDto(AgencyDto Agency, string ManagerMobile, string RoleName);
