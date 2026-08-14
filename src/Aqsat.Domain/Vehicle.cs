using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>Belongs to the policy, not the customer — vehicles change hands and the new owner is innocent.</summary>
public class Vehicle : AgencyOwnedEntity
{
    public string? Plate { get; set; }
    public string? Vin { get; set; }
    public string? Chassis { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
}
