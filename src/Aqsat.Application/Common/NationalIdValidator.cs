namespace Aqsat.Application.Common;

/// <summary>The kind of identity document an input string represents, after normalization.</summary>
public enum IdentityDocKind : byte
{
    /// <summary>Not any recognizable document — invalid input.</summary>
    Unknown = 0,

    /// <summary>A regular Iranian 10-digit national ID (mod-11 checksum valid).</summary>
    IranianNationalId = 1,

    /// <summary>A 10-digit national ID in the 996 series issued to foreign residents.</summary>
    ForeignResidentNationalId = 2,

    /// <summary>A passport number (letter + digits, 5–15 characters).</summary>
    Passport = 3,
}

/// <summary>
/// docs/TASK-25-IDENTITY-VEHICLE.md §1 — the one deliberate exception to "warn, don't block": an
/// invalid national ID must reject the request outright, because it is the identity key Shahkar
/// verification and per-customer attribution both depend on. Only applies to the manual-entry
/// path; imported rows with no national ID are saved with an incomplete-profile flag instead.
/// <para>
/// Owner decision 2026-09-21 (foreign nationals): the validator now CLASSIFIES input into
/// Iranian / 996-series / passport instead of rejecting anything non-Iranian. The 996 series gets
/// a deliberately weaker check — structural only (10 digits, 996 prefix, not all-identical) —
/// because no official checksum algorithm for that series is documented, and a wrong rejection of
/// a real resident is worse than a lenient acceptance: Shahkar is the real proof. Both outcomes
/// are revisable in one place if the official algorithm surfaces.
/// </para>
/// </summary>
public static class NationalIdValidator
{
    /// <summary>Length range of a machine-readable passport number as entered by an agency —
    /// loose on purpose: formats vary by issuing country.</summary>
    private const int PassportMinLength = 5;
    private const int PassportMaxLength = 15;

    /// <summary>The 996 series the Iranian registrar issues to foreign residents.</summary>
    private const string ForeignResidentPrefix = "996";

    /// <summary>True when the input is any acceptable identity document — an Iranian OR a
    /// 996-series ID, or (where a caller accepts passports) a passport number. Existing Iranian
    /// behavior is byte-for-byte unchanged.</summary>
    public static bool IsValid(string? input) => Classify(input) != IdentityDocKind.Unknown;

    /// <summary>True only for a checksum-valid regular Iranian national ID (NOT the 996 series).
    /// Kept for callers that must distinguish — e.g. the identity-kind selector.</summary>
    public static bool IsIranian(string? input) => Classify(input) == IdentityDocKind.IranianNationalId;

    /// <summary>True only for a structurally-valid 996-series foreign-resident national ID.</summary>
    public static bool IsForeignResident(string? input) =>
        Classify(input) == IdentityDocKind.ForeignResidentNationalId;

    /// <summary>True only for a well-formed passport number (letter + digits, 5–15 chars).</summary>
    public static bool IsPassport(string? input) => Classify(input) == IdentityDocKind.Passport;

    /// <summary>Normalizes digits, then classifies the input. Every caller-visible check funnels
    /// through here so the three cases can never drift apart.</summary>
    public static IdentityDocKind Classify(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return IdentityDocKind.Unknown;
        }

        var s = DigitNormalizer.ToLatin(input).Trim().ToUpperInvariant();

        if (s.Length == 10 && s.All(char.IsAsciiDigit))
        {
            // All-identical digits are mathematically valid under the checksum but never real.
            if (s.Distinct().Count() == 1)
            {
                return IdentityDocKind.Unknown;
            }

            if (s.StartsWith(ForeignResidentPrefix, StringComparison.Ordinal))
            {
                // Structural only — see the class doc. The last digit is not verified here.
                return IdentityDocKind.ForeignResidentNationalId;
            }

            var sum = 0;
            for (var i = 0; i < 9; i++)
            {
                sum += (s[i] - '0') * (10 - i);
            }

            var rem = sum % 11;
            var check = s[9] - '0';
            return rem < 2 ? (check == rem ? IdentityDocKind.IranianNationalId : IdentityDocKind.Unknown)
                           : (check == 11 - rem ? IdentityDocKind.IranianNationalId : IdentityDocKind.Unknown);
        }

        // A passport number: a leading Latin letter + alphanumerics, loose length. Letter-first is
        // deliberate — it keeps malformed 996-ish input like "99612345a" out of the passport
        // branch (digit-leading junk has no plausible passport reading here), and pure digits are
        // excluded because that length would already have returned above (restated for intent).
        if (s.Length is >= PassportMinLength and <= PassportMaxLength
            && s.All(c => char.IsAsciiLetterOrDigit(c))
            && char.IsAsciiLetter(s[0]))
        {
            return IdentityDocKind.Passport;
        }

        return IdentityDocKind.Unknown;
    }
}
