namespace Aqsat.Api.Contracts;

public sealed record AuditLogRowDto(
    long Id, string UserDisplayName, string EntityType, string Action, string Description, DateTimeOffset OccurredAt);
