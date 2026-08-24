using System.Text.RegularExpressions;

namespace Aqsat.Application.Common;

/// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §2 — "09" + 9 digits, with +98/0098 prefixes
/// normalized away first.</summary>
public static class MobileNumberValidator
{
    private static readonly Regex Whitespace = new(@"[\s\-]", RegexOptions.Compiled);
    private static readonly Regex ValidShape = new(@"^09\d{9}$", RegexOptions.Compiled);

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var s = Whitespace.Replace(DigitNormalizer.ToLatin(input).Trim(), string.Empty);

        if (s.StartsWith("+98", StringComparison.Ordinal))
        {
            s = "0" + s[3..];
        }
        else if (s.StartsWith("0098", StringComparison.Ordinal))
        {
            s = "0" + s[4..];
        }
        else if (s.StartsWith("98", StringComparison.Ordinal) && s.Length == 12)
        {
            s = "0" + s[2..];
        }

        return s;
    }

    public static bool IsValid(string? input) => ValidShape.IsMatch(Normalize(input));
}
