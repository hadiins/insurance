namespace Aqsat.Domain.Enums;

/// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §5.2 — an agency issues policies for every plate type,
/// not just personal vehicles. Drives both the allowed-letter filter and the plate component's
/// border color.</summary>
public enum PlateType : byte
{
    Personal = 1,
    PublicOrTaxi = 2,
    Government = 3,
    Military = 4,
    AgriculturalOrConstruction = 5,
    DisabilityOrVeteran = 6,
    DiplomaticOrCeremonial = 7,
    CommercialOrFreeZone = 8,
}
