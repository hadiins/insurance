namespace Aqsat.Domain;

/// <summary>Concurrency layer 2 (docs/CONCURRENCY.md) — who has a record open right now.</summary>
public class RecordPresence
{
    public long Id { get; set; }
    public Guid AgencyId { get; set; }

    public string EntityType { get; set; } = default!;
    public Guid EntityId { get; set; }

    public Guid UserId { get; set; }
    public string UserDisplayName { get; set; } = default!;
    public string ConnectionId { get; set; } = default!;

    public DateTimeOffset LastSeenAt { get; set; }
    public bool IsEditing { get; set; }
}
