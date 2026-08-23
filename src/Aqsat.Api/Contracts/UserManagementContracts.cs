namespace Aqsat.Api.Contracts;

public sealed record OrgUserDto(Guid MembershipId, Guid UserId, string FullName, string Mobile, Guid RoleId, string RoleName, bool IsActive);

public sealed record RoleOptionDto(Guid Id, string Name);

public sealed record CreateOrgUserRequest(string FullName, string Mobile, string Password, Guid RoleId);
