using System.Text;

namespace Aqsat.Application.Common;

/// <summary>Persian and Arabic-Indic digits both appear in copy-pasted Fanavaran/user input —
/// docs/TASK-24-POLICY-NUMBER.md §5 and docs/TASK-25-IDENTITY-VEHICLE.md §1 both require this as
/// the first step before any parsing or validation.</summary>
public static class DigitNormalizer
{
    private const string Persian = "۰۱۲۳۴۵۶۷۸۹";
    private const string Arabic = "٠١٢٣٤٥٦٧٨٩";

    public static string ToLatin(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            var persianIndex = Persian.IndexOf(ch);
            if (persianIndex >= 0)
            {
                builder.Append((char)('0' + persianIndex));
                continue;
            }

            var arabicIndex = Arabic.IndexOf(ch);
            if (arabicIndex >= 0)
            {
                builder.Append((char)('0' + arabicIndex));
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
