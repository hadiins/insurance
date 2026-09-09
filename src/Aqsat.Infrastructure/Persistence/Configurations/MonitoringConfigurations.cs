using Aqsat.Domain.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class MetricSampleConfiguration : AqsatEntityConfiguration<MetricSample>
{
    public override void Configure(EntityTypeBuilder<MetricSample> builder)
    {
        base.Configure(builder);

        // The timeseries endpoint scans a window ending "now" — a descending-friendly index on the
        // minute makes every dashboard query a short seek, and uniqueness keeps a double-running
        // sampler from inserting the same minute twice.
        builder.HasIndex(s => s.MinuteUtc).IsUnique();
    }
}

public sealed class EndpointStatConfiguration : AqsatEntityConfiguration<EndpointStat>
{
    public override void Configure(EntityTypeBuilder<EndpointStat> builder)
    {
        base.Configure(builder);

        builder.Property(s => s.Path).HasMaxLength(300);

        builder.HasIndex(s => s.MinuteUtc);
        builder.HasIndex(s => new { s.MinuteUtc, s.Path }).IsUnique();
    }
}

public sealed class SecurityEventConfiguration : AqsatEntityConfiguration<SecurityEvent>
{
    public override void Configure(EntityTypeBuilder<SecurityEvent> builder)
    {
        base.Configure(builder);

        builder.Property(e => e.Detail).HasMaxLength(500);
        builder.Property(e => e.IpAddress).HasMaxLength(64);
        builder.Property(e => e.Mobile).HasMaxLength(20);

        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => e.Type);

        // Brute-force detection groups failed logins by IP over a 15-minute window.
        builder.HasIndex(e => new { e.Type, e.IpAddress, e.OccurredAt });
    }
}

public sealed class AlertRuleConfiguration : AqsatEntityConfiguration<AlertRule>
{
    public override void Configure(EntityTypeBuilder<AlertRule> builder)
    {
        base.Configure(builder);

        builder.Property(r => r.Name).HasMaxLength(120);
    }
}

public sealed class AlertOccurrenceConfiguration : AqsatEntityConfiguration<AlertOccurrence>
{
    public override void Configure(EntityTypeBuilder<AlertOccurrence> builder)
    {
        base.Configure(builder);

        builder.Property(o => o.Message).HasMaxLength(500);

        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => o.StartedAt);

        builder.HasOne(o => o.Rule)
            .WithMany()
            .HasForeignKey(o => o.RuleId);
    }
}
