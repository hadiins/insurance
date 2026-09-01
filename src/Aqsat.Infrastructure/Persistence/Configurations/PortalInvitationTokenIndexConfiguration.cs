using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class PortalInvitationTokenIndexConfiguration : AqsatEntityConfiguration<PortalInvitationTokenIndex>
{
    public override void Configure(EntityTypeBuilder<PortalInvitationTokenIndex> builder)
    {
        base.Configure(builder);

        builder.Property(t => t.Token).HasMaxLength(43).IsRequired();

        // One token resolves to one agency — the anonymous entry point, hence globally unique
        // (no AgencyId prefix: this table is RLS-exempt by design).
        builder.HasIndex(t => t.Token).IsUnique();

        // Lookup path: token -> agency; then agency + token -> invitation.
        builder.HasIndex(t => new { t.AgencyId, t.Token });
    }
}
