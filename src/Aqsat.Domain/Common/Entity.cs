namespace Aqsat.Domain.Common;

/// <summary>
/// Sequential-GUID PK (non-clustered) + IDENTITY BizId (clustered). Configured centrally in
/// Aqsat.Infrastructure's AqsatEntityConfiguration&lt;T&gt; base — never repeat this per entity.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; }
    public long BizId { get; set; }
}
