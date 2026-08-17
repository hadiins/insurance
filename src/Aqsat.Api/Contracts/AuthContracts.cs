namespace Aqsat.Api.Contracts;

public sealed record LoginRequest(string Mobile, string Password);

public sealed record LoginResponse(string Token, int ExpiresInMinutes);

public sealed record OrganizationMembership(Guid OrganizationId, string OrganizationName, string RoleName);

public sealed record MeResponse(
    Guid UserId,
    string DisplayName,
    Guid ActiveOrganizationId,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<OrganizationMembership> Organizations);
