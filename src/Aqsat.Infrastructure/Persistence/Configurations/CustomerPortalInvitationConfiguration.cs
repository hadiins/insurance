using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class CustomerPortalInvitationConfiguration : AqsatEntityConfiguration<CustomerPortalInvitation>
{
    public override void Configure(EntityTypeBuilder<CustomerPortalInvitation> builder)
    {
        base.Configure(builder);

        builder.Property(i => i.Token).HasMaxLength(43).IsRequired();

        // The public portal's only entry point: token lookup, AgencyId leading (rule 3). Filtered
        // so a soft-deleted row's token can never resolve.
        builder.HasIndex(i => new { i.AgencyId, i.Token })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne(i => i.Customer)
            .WithMany()
            .HasForeignKey(i => i.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Every FK gets its own index (rule 5).
        builder.HasIndex(i => new { i.AgencyId, i.CustomerId });
    }
}
