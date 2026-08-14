using Aqsat.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Persistence;

/// <summary>
/// Because RLS already hides cross-tenant rows on this connection, "the row doesn't exist" and
/// "the row exists but belongs to another agency" collapse into the same, correct answer — no
/// extra AgencyId comparison is needed here. Only usable for entities keyed on a Guid Id
/// (Aqsat.Domain.Common.Entity), which covers every operational, RLS-scoped table.
/// </summary>
public sealed class ScopeGuard(AppDbContext context) : IScopeGuard
{
    public async Task EnsureExistsInScopeAsync<TEntity>(Guid id, CancellationToken cancellationToken = default)
        where TEntity : class
    {
        var exists = await context.Set<TEntity>()
            .AsNoTracking()
            .Where(e => EF.Property<Guid>(e, "Id") == id)
            .AnyAsync(cancellationToken);

        if (!exists)
        {
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name} '{id}' was not found in the current agency's scope.");
        }
    }
}
