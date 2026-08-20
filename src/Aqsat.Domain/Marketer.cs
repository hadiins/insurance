using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// Profile and login are deliberately separate entities — many marketers only receive SMS and never
/// log in (docs/PHASE-1-SPEC.md §2.2). A marketer is a security boundary, not just a feature: see
/// the "Marketer access" section of CLAUDE.md for the hard limits on what they may see.
/// </summary>
public class Marketer : AgencyOwnedEntity
{
    public string FullName { get; set; } = default!;
    public string Mobile { get; set; } = default!;

    [AuditSensitive]
    public string? NationalId { get; set; }

    public MarketerType Type { get; set; }

    /// <summary>Optional — set only for the marketers who actually log in.</summary>
    public Guid? AppUserId { get; set; }
    public AppUser? AppUser { get; set; }

    public bool IsActive { get; set; } = true;
}
