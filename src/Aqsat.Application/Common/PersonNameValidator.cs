namespace Aqsat.Application.Common;

/// <summary>Production incident 2026-09-07 (نمایندگی ماهرو): a national ID typed into the
/// lastName field — digits-only "names" are always a field-order mistake, never a real name.
/// Persian and Arabic-Indic digit forms are both checked.</summary>
public static class PersonNameValidator
{
    public static bool IsDigitsOnly(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var s = DigitNormalizer.ToLatin(input.Trim());
        return s.Length > 0 && s.All(char.IsAsciiDigit);
    }
}
