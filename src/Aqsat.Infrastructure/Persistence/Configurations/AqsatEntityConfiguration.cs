using Aqsat.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

/// <summary>
/// Wires the Id/BizId pattern once for every entity that uses it (CLAUDE.md rule 1): a sequential
/// GUID primary key that is deliberately NOT clustered, and a separate IDENTITY BizId column that
/// carries the clustered index. Random GUIDs as clustered keys destroy insert performance.
/// </summary>
public abstract class AqsatEntityConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : Entity
{
    public virtual void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.HasKey(e => e.Id).IsClustered(false);

        builder.Property(e => e.Id)
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("NEWSEQUENTIALID()");

        builder.Property(e => e.BizId)
            .UseIdentityColumn();

        builder.HasIndex(e => e.BizId).IsUnique().IsClustered();

        if (typeof(SoftDeletableEntity).IsAssignableFrom(typeof(TEntity)))
        {
            builder.Property<byte[]>(nameof(SoftDeletableEntity.RowVersion)).IsRowVersion();
        }

        if (typeof(AgencyOwnedEntity).IsAssignableFrom(typeof(TEntity)))
        {
            builder.HasIndex(nameof(AgencyOwnedEntity.AgencyId));
        }
    }
}
