namespace Aqsat.Application.Common;

/// <summary>
/// RLS is not enough — a FK to a row outside the caller's scope does not throw at the database
/// level, it silently succeeds. The service layer must independently verify every referenced ID is
/// inside the caller's scope before using it (CLAUDE.md rule 11).
/// </summary>
public interface IScopeGuard
{
    /// <summary>Throws if no row of type TEntity with this Id is visible in the current scope.</summary>
    Task EnsureExistsInScopeAsync<TEntity>(Guid id, CancellationToken cancellationToken = default)
        where TEntity : class;
}
