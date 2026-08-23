namespace Aqsat.Api.Contracts;

public sealed record PermissionCatalogItemDto(string Key, string Label);

public sealed record RoleDto(Guid Id, string Name, IReadOnlyList<string> Permissions, bool IsSystemRole, int MemberCount);

public sealed record SaveRoleRequest(string Name, IReadOnlyList<string> Permissions);
