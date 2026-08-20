using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// Global/system-wide, not per-agency — every agency picks from the same list. Self-referencing so
/// مسئولیت can carry sub-types (کارفرما، حرفه‌ای، عمومی). A policy's subject varies by line: do not
/// force VehicleId onto every policy — Policy.VehicleId/PropertySubjectId stay nullable, gated by
/// RequiresVehicle/RequiresProperty at the service layer (CLAUDE.md "Multi-line from day one").
/// </summary>
public class InsuranceLine : SoftDeletableEntity
{
    public Guid? ParentId { get; set; }
    public InsuranceLine? Parent { get; set; }

    public string Code { get; set; } = default!;
    public string NameFa { get; set; } = default!;

    public bool RequiresVehicle { get; set; }
    public bool RequiresProperty { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
