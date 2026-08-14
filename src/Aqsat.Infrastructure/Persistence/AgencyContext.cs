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
}

public sealed class AgencyContextAccessor : ICurrentAgencyAccessor
{
    public Guid? AgencyId => AgencyContext.Current;
}
