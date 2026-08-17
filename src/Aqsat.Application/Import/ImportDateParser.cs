using System.Globalization;

namespace Aqsat.Application.Import;

/// <summary>
/// Parses a raw date-like cell value against a format the operator explicitly confirmed at commit
/// time (never the preview's heuristic guess). Uses the BCL's PersianCalendar — already part of
/// .NET, no new package needed.
/// </summary>
public static class ImportDateParser
{
    private static readonly PersianCalendar Persian = new();
    private static readonly char[] Separators = ['/', '-'];

    public static bool TryParse(string? raw, DetectedDateFormat format, out DateOnly result)
    {
        result = default;
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var parts = value.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var first)
            || !int.TryParse(parts[1], out var month)
            || !int.TryParse(parts[2], out var day))
        {
            return false;
        }

        try
        {
            if (format == DetectedDateFormat.Jalali)
            {
                var dateTime = Persian.ToDateTime(first, month, day, 0, 0, 0, 0);
                result = DateOnly.FromDateTime(dateTime);
            }
            else
            {
                result = new DateOnly(first, month, day);
            }

            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
