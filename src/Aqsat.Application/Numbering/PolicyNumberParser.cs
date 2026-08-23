using System.Text.RegularExpressions;
using Aqsat.Application.Common;
using Aqsat.Domain;

namespace Aqsat.Application.Numbering;

/// <summary>docs/TASK-24-POLICY-NUMBER.md §5. Serial is a string everywhere on purpose — a
/// round-trip through int would turn "000001" into "1" and the number would stop matching
/// Fanavaran's.</summary>
public sealed record PolicyNumberParts(
    string Raw, string? LineCode, string? AgencyCode, int? Year, string? Serial, bool IsParsed, string? Note);

/// <summary>Parsing never rejects a policy — a format Fanavaran changes tomorrow must not stop the
/// system from saving PolicyNumber; it only stops the derived Pn* columns from being populated
/// (§5: "اگر شرکت بیمه فردا فرمت را عوض کند، سیستم نباید بخوابد").</summary>
public static class PolicyNumberParser
{
    private static readonly Regex AlternateSeparators = new(@"[\-\.\\]", RegexOptions.Compiled);
    private static readonly Regex InvisibleOrDirectional = new("[‌‎‏\\s]", RegexOptions.Compiled);

    public static string Normalize(string raw)
    {
        var latin = DigitNormalizer.ToLatin(raw);
        var noInvisible = InvisibleOrDirectional.Replace(latin, string.Empty);
        return AlternateSeparators.Replace(noInvisible, "/");
    }

    public static PolicyNumberParts Parse(string raw, PolicyNumberFormat format)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new PolicyNumberParts(raw, null, null, null, null, false, "شمارهٔ خالی است.");
        }

        var separator = string.IsNullOrEmpty(format.Separator) ? "/" : format.Separator;
        var normalized = Normalize(raw);
        var parts = normalized.Split(separator, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 4)
        {
            return new PolicyNumberParts(raw, null, null, null, null, false, $"انتظار ۴ بخش می‌رفت، {parts.Length} بخش یافت شد.");
        }

        var (lineCode, agencyCode, yearRaw, serial) = (parts[0], parts[1], parts[2], parts[3]);

        if (!int.TryParse(yearRaw, out var yearDigits))
        {
            return new PolicyNumberParts(raw, lineCode, agencyCode, null, serial, false, $"سال «{yearRaw}» عددی نیست.");
        }

        var (year, note) = NormalizeYear(yearDigits);
        return new PolicyNumberParts(raw, lineCode, agencyCode, year, serial, true, note);
    }

    /// <summary>§5 — 4-digit stays as-is; 3-digit becomes 1000+n (405 → 1405); 2-digit is accepted
    /// with a note (05 → 1405) since it is ambiguous by nature.</summary>
    private static (int Year, string? Note) NormalizeYear(int digits) => digits switch
    {
        >= 1000 and <= 9999 => (digits, null),
        >= 100 and <= 999 => (1000 + digits, null),
        >= 0 and <= 99 => (1400 + digits, "سال دورقمی بود؛ با فرض ۱۴۰۰+ن تفسیر شد."),
        _ => (digits, "سال خارج از بازهٔ معمول است."),
    };

    public static string Compose(string lineCode, string agencyCode, int year, string serial, PolicyNumberFormat format)
    {
        var separator = string.IsNullOrEmpty(format.Separator) ? "/" : format.Separator;
        var yearPart = format.YearDigits == 3 ? (year % 1000).ToString("D3") : year.ToString("D4");
        return string.Join(separator, lineCode, agencyCode, yearPart, serial);
    }
}
