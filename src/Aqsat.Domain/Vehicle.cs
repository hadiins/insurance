using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// Belongs to the policy, not the customer — vehicles change hands and the new owner is innocent.
/// docs/TASK-25-IDENTITY-VEHICLE.md §4/§5 adds structured plate fields and a few renamed/new
/// columns alongside the originals; Plate/Chassis/Make/Model/Year stay as-is here (this migration
/// is purely additive) — the background parser reads the legacy Plate string into the new
/// structured columns, and consolidating Make+Model into VehicleType is application-code work for
/// step 5, not a schema concern.
/// </summary>
public class Vehicle : AgencyOwnedEntity
{
    public string? Plate { get; set; }
    public string? Vin { get; set; }
    public string? Chassis { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }

    public PlateType PlateType { get; set; } = PlateType.Personal;
    public string? PlateTwoDigit { get; set; }
    public string? PlateLetter { get; set; }
    public string? PlateThreeDigit { get; set; }
    public string? PlateIranCode { get; set; }

    /// <summary>Computed from the four parts above, e.g. "55الف555-55" — for search and the
    /// (deliberately non-unique, §5.3) plate index.</summary>
    public string? PlateNormalized { get; set; }

    public string? EngineNumber { get; set; }

    /// <summary>Free text ("پژو ۲۰۶") rather than a closed list — §4's own rationale: a closed
    /// vehicle-model list goes stale and locks users out.</summary>
    public string? VehicleType { get; set; }

    public int? ManufactureYear { get; set; }
}
