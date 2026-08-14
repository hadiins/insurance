namespace Aqsat.Domain.Common;

/// <summary>
/// Every operational table has AgencyId. Isolation is enforced by SQL Server Row-Level Security on
/// this column, never by an EF query filter — the service layer must not rely on a repository
/// Where(x => x.AgencyId == ...) as the isolation mechanism.
/// </summary>
public abstract class AgencyOwnedEntity : SoftDeletableEntity
{
    public Guid AgencyId { get; set; }
}
