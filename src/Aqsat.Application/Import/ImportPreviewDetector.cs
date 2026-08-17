namespace Aqsat.Application.Import;

public enum DetectedDateFormat
{
    Unknown,
    Jalali,
    Gregorian,
}

public enum DetectedCurrencyScale
{
    Unknown,
    LikelyRials,
    LikelyToman,
}

/// <summary>
/// Purely advisory, per-column heuristics shown in the import preview (docs/PHASE-1-SPEC.md §4.3:
/// "detected currency and date format... surfaced in the preview"). Never trusted silently for the
/// actual conversion — CLAUDE.md rule 19 requires the unit to be shown, and ImportService only
/// applies the rials-to-toman conversion when the operator explicitly confirms it at commit time,
/// not from this guess.
/// </summary>
public static class ImportPreviewDetector
{
    private static readonly char[] DateSeparators = ['/', '-'];

    public static DetectedDateFormat DetectDateFormat(IEnumerable<string> sampleValues)
    {
        var jalaliCount = 0;
        var gregorianCount = 0;

        foreach (var raw in sampleValues)
        {
            var value = raw?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var parts = value.Split(DateSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !int.TryParse(parts[0], out var leadingNumber))
            {
                continue;
            }

            if (leadingNumber is >= 1300 and <= 1500)
            {
                jalaliCount++;
            }
            else if (leadingNumber is >= 1900 and <= 2100)
            {
                gregorianCount++;
            }
        }

        if (jalaliCount == 0 && gregorianCount == 0)
        {
            return DetectedDateFormat.Unknown;
        }

        return jalaliCount >= gregorianCount ? DetectedDateFormat.Jalali : DetectedDateFormat.Gregorian;
    }

    public static DetectedCurrencyScale DetectCurrencyScale(IEnumerable<string> sampleValues)
    {
        var numbers = new List<decimal>();
        foreach (var raw in sampleValues)
        {
            var cleaned = raw?.Replace(",", "").Trim();
            if (!string.IsNullOrEmpty(cleaned) && decimal.TryParse(cleaned, out var value) && value > 0)
            {
                numbers.Add(value);
            }
        }

        if (numbers.Count == 0)
        {
            return DetectedCurrencyScale.Unknown;
        }

        // Real premiums in toman are typically hundreds-of-thousands to low-millions; the same
        // amount in rials is 10x that. A rough magnitude split, nothing more.
        var average = numbers.Average();
        return average >= 5_000_000m ? DetectedCurrencyScale.LikelyRials : DetectedCurrencyScale.LikelyToman;
    }
}
