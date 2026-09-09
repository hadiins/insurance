using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class ManualReviewConfiguration : AqsatEntityConfiguration<ManualReview>
{
    public override void Configure(EntityTypeBuilder<ManualReview> builder)
    {
        base.Configure(builder);

        builder.HasOne(r => r.Customer)
            .WithMany()
            .HasForeignKey(r => r.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Assessment)
            .WithMany()
            .HasForeignKey(r => r.AssessmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.AssignedToUser)
            .WithMany()
            .HasForeignKey(r => r.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.AgencyId, r.Status, r.CreatedAt }).IsDescending();
        builder.Property(r => r.Note).HasMaxLength(1024);
    }
}
