using Aqsat.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class VehicleConfiguration : AqsatEntityConfiguration<Vehicle>
{
    public override void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        base.Configure(builder);

        builder.Property(v => v.Plate).HasMaxLength(20);
        builder.Property(v => v.Vin).HasMaxLength(30);
        builder.Property(v => v.Chassis).HasMaxLength(30);
        builder.Property(v => v.Make).HasMaxLength(60);
        builder.Property(v => v.Model).HasMaxLength(60);
    }
}
