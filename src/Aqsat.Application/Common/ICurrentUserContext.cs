namespace Aqsat.Application.Common;

/// <summary>
/// Who is calling, resolved fresh from the database on every request by
/// ScopeResolutionMiddleware — never trusted straight from the JWT, so a permission change takes
/// effect on the caller's very next request instead of only after their token expires.
/// </summary>
public interface ICurrentUserContext
{
    Guid UserId { get; }
    string DisplayName { get; }
    Guid ActiveOrganizationId { get; }
    IReadOnlySet<string> Permissions { get; }
}
