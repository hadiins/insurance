using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>docs/TASK-24-POLICY-NUMBER.md §4.2 — the format is the same across insurers today but
/// must not be hard-coded, since it is theirs to change, not ours.</summary>
public class PolicyNumberFormat : AgencyOwnedEntity
{
    public string InsurerName { get; set; } = default!;
    public string Pattern { get; set; } = default!;
    public string Separator { get; set; } = default!;

    public int LineCodeLength { get; set; }
    public int AgencyCodeLength { get; set; }
    public int YearDigits { get; set; }
    public int SerialLength { get; set; }

    public bool IsStrict { get; set; }
    public bool IsActive { get; set; } = true;
}
