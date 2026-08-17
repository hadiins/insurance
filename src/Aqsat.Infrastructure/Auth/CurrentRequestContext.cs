namespace Aqsat.Infrastructure.Auth;

/// <summary>
/// AsyncLocal-backed, same pattern as AgencyContext/CurrentUserContext — set by
/// ScopeResolutionMiddleware from the incoming connection, read by AppDbContext's audit override
/// to populate AuditEntry.IpAddress without threading IHttpContextAccessor into the DbContext
/// constructor (which every test/seeder that builds AppDbContext directly would then need to
/// supply).
/// </summary>
public static class CurrentRequestContext
{
    private static readonly AsyncLocal<string?> RemoteIpAddress = new();

    public static string? IpAddress
    {
        get => RemoteIpAddress.Value;
        set => RemoteIpAddress.Value = value;
    }
}
