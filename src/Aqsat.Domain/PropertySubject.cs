using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>The insured subject for property lines (آتش‌سوزی and similar) — attached to the policy,
/// the same way Vehicle is for ثالث/بدنه. Postal code is stored here deliberately: fire-insurance
/// underwriting needs it (CLAUDE.md rule 13 allows storage "unless a concrete feature needs it").</summary>
public class PropertySubject : AgencyOwnedEntity
{
    public string Address { get; set; } = default!;
    public string? PostalCode { get; set; }
    public string? Type { get; set; }
    public decimal? Value { get; set; }
}
