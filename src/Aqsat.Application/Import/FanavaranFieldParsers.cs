namespace Aqsat.Application.Import;

/// <summary>
/// Field-level parsing specific to the Fanavaran policy report (docs/PHASE-1-SPEC.md §4.1) — the
/// two string transformations that make Task 7 "a concrete adapter over Task 6" rather than a
/// second copy of the import pipeline.
/// </summary>
public static class FanavaranFieldParsers
{
    private const string CodeMarker = "کد";
    private const string ContractNumberMarker = "شماره قرارداد";

    /// <summary>"نام خانوادگی کد 8030987" → ("نام خانوادگی", "8030987"). If the marker isn't
    /// present, the whole value is returned as the name with no code — a malformed but non-fatal
    /// case the row validation downstream will reject (no ExternalCode) rather than silently guessing.</summary>
    public static (string FullName, string? ExternalCode) ParseInsuredNameAndCode(string raw)
    {
        var value = raw.Trim();
        var markerIndex = value.LastIndexOf(CodeMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return (value, null);
        }

        var name = value[..markerIndex].Trim();
        var code = value[(markerIndex + CodeMarker.Length)..].Trim();
        return (name.Length == 0 ? value : name, code.Length == 0 ? null : code);
    }

    /// <summary>Strips everything from «شماره قرارداد» onward, then hands the remainder to
    /// ContractTemplateMatcher — never search for «اقساطی» itself.</summary>
    public static string StripContractNumber(string raw)
    {
        var value = raw.Trim();
        var markerIndex = value.IndexOf(ContractNumberMarker, StringComparison.Ordinal);
        return (markerIndex < 0 ? value : value[..markerIndex]).Trim();
    }
}
