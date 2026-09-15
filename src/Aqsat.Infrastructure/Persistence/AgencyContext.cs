using Aqsat.Application.Common;

namespace Aqsat.Infrastructure.Persistence;

/// <summary>
/// AsyncLocal-backed current-agency store. Task 4's auth middleware sets <see cref="Current"/> from
/// the authenticated user's JWT at the start of each request; tests and the dev seeder set it
/// directly to exercise RLS without a full auth pipeline.
/// </summary>
public static class AgencyContext
{
    private static readonly AsyncLocal<Guid?> CurrentAgencyId = new();

    public static Guid? Current
    {
        get => CurrentAgencyId.Value;
        set => CurrentAgencyId.Value = value;
    }

    /// <summary>
    /// Scoped assignment with automatic restore — the RLS-safe way to work inside a specific
    /// agency outside a request (jobs, the public portal token flow):
    /// <code>using (AgencyContext.BeginScope(agencyId)) { ... }</code>
    /// A bare <c>Current = agencyId</c> leaks the value to whatever ran after the block in the
    /// same async flow (CLAUDE.md #12/#17: a leaked scope is indistinguishable from no scope);
    /// the previous value is restored even on exception.
    /// </summary>
    public static IDisposable BeginScope(Guid agencyId)
    {
        var previous = Current;
        Current = agencyId;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(Guid? previous) : IDisposable
    {
        public void Dispose() => Current = previous;
    }
}

public sealed class AgencyContextAccessor : ICurrentAgencyAccessor
{
    public Guid? AgencyId => AgencyContext.Current;
}
