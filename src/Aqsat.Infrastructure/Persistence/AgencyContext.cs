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
    private static readonly AsyncLocal<bool> HierarchyReadScope = new();

    public static Guid? Current
    {
        get => CurrentAgencyId.Value;
        set => CurrentAgencyId.Value = value;
    }

    /// <summary>
    /// When true, the RLS FILTER predicate widens from <see cref="Current"/> to that organization's
    /// whole subtree (migration AddAgencyHierarchyReadScope): a Regional or Headquarters tenant reads
    /// the rows of every tenant below it. Writes never widen — the policy's BLOCK predicates stay on
    /// the strict-equality function, so a widened session cannot insert into or update a subordinate
    /// agency's rows.
    ///
    /// Default false on purpose. Every self-served agency is parented under the single Headquarters
    /// org, so widening is a deliberate, per-request decision and never an ambient surprise; whoever
    /// sets <see cref="Current"/> for a request must assign this too rather than rely on the default.
    /// </summary>
    public static bool HierarchyRead
    {
        get => HierarchyReadScope.Value;
        set => HierarchyReadScope.Value = value;
    }

    /// <summary>
    /// Scoped assignment with automatic restore — the RLS-safe way to work inside a specific
    /// agency outside a request (jobs, the public portal token flow):
    /// <code>using (AgencyContext.BeginScope(agencyId)) { ... }</code>
    /// A bare <c>Current = agencyId</c> leaks the value to whatever ran after the block in the
    /// same async flow (CLAUDE.md #12/#17: a leaked scope is indistinguishable from no scope);
    /// the previous value is restored even on exception. <see cref="HierarchyRead"/> is restored
    /// the same way, so a widened read can never outlive the block that asked for it.
    /// </summary>
    public static IDisposable BeginScope(Guid agencyId, bool hierarchyRead = false)
    {
        var previous = Current;
        var previousHierarchy = HierarchyRead;
        Current = agencyId;
        HierarchyRead = hierarchyRead;
        return new RestoreScope(previous, previousHierarchy);
    }

    private sealed class RestoreScope(Guid? previous, bool previousHierarchy) : IDisposable
    {
        public void Dispose()
        {
            Current = previous;
            HierarchyRead = previousHierarchy;
        }
    }
}

public sealed class AgencyContextAccessor : ICurrentAgencyAccessor
{
    public Guid? AgencyId => AgencyContext.Current;
}
