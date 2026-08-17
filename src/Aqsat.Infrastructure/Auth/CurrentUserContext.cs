using Aqsat.Application.Common;

namespace Aqsat.Infrastructure.Auth;

/// <summary>AsyncLocal-backed, populated by ScopeResolutionMiddleware — mirrors AgencyContext.</summary>
public static class CurrentUserContext
{
    private static readonly AsyncLocal<ResolvedUser?> CurrentUser = new();

    public static ResolvedUser? Current
    {
        get => CurrentUser.Value;
        set => CurrentUser.Value = value;
    }
}

public sealed record ResolvedUser(Guid UserId, string DisplayName, Guid ActiveOrganizationId, IReadOnlySet<string> Permissions);

public sealed class CurrentUserContextAccessor : ICurrentUserContext
{
    private ResolvedUser Resolved =>
        CurrentUserContext.Current ?? throw new InvalidOperationException(
            "No resolved user in the current context — ScopeResolutionMiddleware must run before this is used.");

    public Guid UserId => Resolved.UserId;
    public string DisplayName => Resolved.DisplayName;
    public Guid ActiveOrganizationId => Resolved.ActiveOrganizationId;
    public IReadOnlySet<string> Permissions => Resolved.Permissions;
}
