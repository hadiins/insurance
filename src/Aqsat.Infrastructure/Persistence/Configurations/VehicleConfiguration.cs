using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Microsoft.EntityFrameworkCore;
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

        builder.Property(v => v.PlateTwoDigit).HasMaxLength(2);
        builder.Property(v => v.PlateLetter).HasMaxLength(10);
        builder.Property(v => v.PlateThreeDigit).HasMaxLength(3);
        builder.Property(v => v.PlateIranCode).HasMaxLength(2);
        builder.Property(v => v.PlateNormalized).HasMaxLength(30);
        builder.Property(v => v.EngineNumber).HasMaxLength(30);
        builder.Property(v => v.VehicleType).HasMaxLength(80);
        builder.Property(v => v.PlateType).HasDefaultValue(PlateType.Personal);

        // Deliberately not unique (docs/TASK-25-IDENTITY-VEHICLE.md §5.3) — the same plate can
        // appear on more than one Vehicle row across years/ownership changes.
        builder.HasIndex(v => new { v.AgencyId, v.PlateNormalized }).HasFilter("[IsDeleted] = 0");
    }
}
