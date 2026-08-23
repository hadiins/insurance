namespace Aqsat.Application.Common;

/// <summary>
/// docs/TASK-25-IDENTITY-VEHICLE.md §1 — the one deliberate exception to "warn, don't block": an
/// invalid national ID must reject the request outright, because it is the identity key Shahkar
/// verification and per-customer attribution both depend on. Only applies to the manual-entry
/// path; imported rows with no national ID are saved with an incomplete-profile flag instead.
/// </summary>
public static class NationalIdValidator
{
    public static bool IsValid(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var s = DigitNormalizer.ToLatin(input).Trim();
        if (s.Length != 10 || !s.All(char.IsAsciiDigit))
        {
            return false;
        }

        // All-identical digits are mathematically valid under the checksum but never real.
        if (s.Distinct().Count() == 1)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (s[i] - '0') * (10 - i);
        }

        var rem = sum % 11;
        var check = s[9] - '0';
        return rem < 2 ? check == rem : check == 11 - rem;
    }
}
