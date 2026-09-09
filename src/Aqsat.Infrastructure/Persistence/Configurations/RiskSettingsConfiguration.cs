using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

/// <summary>1:1 extension of Organization keyed directly on OrganizationId — OrgSettings pattern.</summary>
public sealed class RiskSettingsConfiguration : IEntityTypeConfiguration<RiskSettings>
{
    public void Configure(EntityTypeBuilder<RiskSettings> builder)
    {
        builder.HasKey(s => s.OrganizationId);

        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasOne(s => s.Organization)
            .WithOne()
            .HasForeignKey<RiskSettings>(s => s.OrganizationId);
    }
}
