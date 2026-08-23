using System.Text.RegularExpressions;
using Aqsat.Application.Common;

namespace Aqsat.Application.Numbering;

public sealed record PlateParts(
    string Raw, string? TwoDigit, string? Letter, string? ThreeDigit, string? IranCode,
    string? Normalized, bool IsParsed, string? Note);

/// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §5.4 — Fanavaran's plate text doesn't match our
/// format, and isn't even internally consistent (the "ایران"/"IR" marker is sometimes present,
/// sometimes the whole block is reversed). Failure never rejects the row (§5.4, verbatim) — the
/// raw plate stays on Vehicle.Plate regardless.</summary>
public static class PlateParser
{
    private static readonly string[] PersianLetters =
        ["الف", "ب", "پ", "ت", "ث", "ج", "د", "ز", "ژ", "س", "ش", "ص", "ط", "ع", "ف", "ق", "ک", "گ", "ل", "م", "ن", "و", "ه", "ی"];

    private static readonly Dictionary<string, string> LatinLetterMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ALEF"] = "الف", ["B"] = "ب", ["P"] = "پ", ["T"] = "ت", ["TH"] = "ث", ["J"] = "ج", ["D"] = "د",
        ["ZH"] = "ژ", ["Z"] = "ز", ["SH"] = "ش", ["S"] = "س", ["SA"] = "ص", ["TA"] = "ط", ["EIN"] = "ع",
        ["E"] = "ع", ["F"] = "ف", ["GH"] = "ق", ["Q"] = "ق", ["K"] = "ک", ["G"] = "گ", ["L"] = "ل",
        ["M"] = "م", ["N"] = "ن", ["V"] = "و", ["W"] = "و", ["H"] = "ه", ["Y"] = "ی",
    };

    private static readonly string LetterAlternation =
        string.Join("|", PersianLetters.Concat(LatinLetterMap.Keys).OrderByDescending(s => s.Length).Select(Regex.Escape));

    private static readonly Regex Forward = new(
        $@"(?<two>\d{{2}})\D*?(?<letter>{LetterAlternation})\D*?(?<three>\d{{3}})\D*?(?:ایران|IR)?\D*?(?<iran>\d{{2}})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Reversed order ("ایران ۵۵ - ۵۵۵ الف ۵۵") doesn't just move the marker to the front — the
    // whole block reads back-to-front, so three-digit and two-digit swap places too. Only
    // distinguishable from the forward format by the marker appearing first; without it, requiring
    // the marker keeps this from misreading an ordinary forward plate.
    private static readonly Regex Backward = new(
        $@"(?:ایران|IR)\D*?(?<iran>\d{{2}})\D*?(?<three>\d{{3}})\D*?(?<letter>{LetterAlternation})\D*?(?<two>\d{{2}})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Invisible = new("[‌‎‏]", RegexOptions.Compiled);

    public static PlateParts Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Fail(raw ?? string.Empty, "پلاک خالی است.");
        }

        var normalized = Invisible.Replace(DigitNormalizer.ToLatin(raw).Trim(), string.Empty);

        var match = Forward.Match(normalized);
        if (!match.Success)
        {
            match = Backward.Match(normalized);
        }

        if (!match.Success)
        {
            return Fail(raw, "قالب پلاک شناخته نشد.");
        }

        var letterToken = match.Groups["letter"].Value;
        var letter = PersianLetters.Contains(letterToken)
            ? letterToken
            : LatinLetterMap.GetValueOrDefault(letterToken, letterToken);

        var two = match.Groups["two"].Value;
        var three = match.Groups["three"].Value;
        var iran = match.Groups["iran"].Value;
        var composed = $"{two}{letter}{three}-{iran}";

        return new PlateParts(raw, two, letter, three, iran, composed, true, null);
    }

    private static PlateParts Fail(string raw, string note) => new(raw, null, null, null, null, null, false, note);
}
