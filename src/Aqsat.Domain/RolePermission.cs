using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// One row per permission granted to a role. Phase 1 permissions (docs/PHASE-1-SPEC.md §2):
/// Policy.Read, Policy.Write, Payment.Write, Import.Run, Settings.Write, Lock.ForceRelease,
/// Report.Read.
/// </summary>
public class RolePermission : SoftDeletableEntity
{
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = default!;

    public string Permission { get; set; } = default!;
}
