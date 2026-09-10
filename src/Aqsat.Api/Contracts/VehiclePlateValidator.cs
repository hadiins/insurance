using Aqsat.Application.Common;

namespace Aqsat.Api.Contracts;

/// <summary>
/// B15 — the plate component's four structured inputs are only digit-normalized on the server;
/// "ab" would land in PlateTwoDigit and poison every plate search that keys on the parts. The
/// free-text Plate path stays untouched (legacy/import rows carry unparseable plates on purpose).
/// Allowed letters mirror PlateField's dropdowns: the normal letters plus the type-specific
/// الف/ژ and the diplomatic Latin D/S.
/// </summary>
public static class VehiclePlateValidator
{
    private static readonly string[] NormalLetters =
        ["ب", "پ", "ت", "ث", "ج", "د", "ز", "ژ", "س", "ش", "ص", "ط", "ع", "ف", "ق", "ک", "گ", "ل", "م", "ن", "و", "ه", "ی"];

    public static string? Validate(VehicleInput vehicle)
    {
        var two = DigitNormalizer.ToLatin(vehicle.PlateTwoDigit ?? string.Empty);
        var three = DigitNormalizer.ToLatin(vehicle.PlateThreeDigit ?? string.Empty);
        var iran = DigitNormalizer.ToLatin(vehicle.PlateIranCode ?? string.Empty);
        var letter = vehicle.PlateLetter?.Trim() ?? string.Empty;

        var hasAnyPart = two.Length > 0 || three.Length > 0 || iran.Length > 0 || letter.Length > 0;
        if (!hasAnyPart)
        {
            return null;
        }

        // The composed plate is only derived when all four parts are present — anything less
        // silently drops the plate altogether, so refuse the half-filled state up front.
        if (two.Length != 2 || three.Length != 3 || iran.Length != 2 || letter.Length == 0)
        {
            return "پلاک ساخت‌یافته ناقص است: دو رقم، حرف، سه رقم و کد ایران همگی الزامی‌اند.";
        }

        if (two.Any(c => !char.IsAsciiDigit(c)) || three.Any(c => !char.IsAsciiDigit(c)) || iran.Any(c => !char.IsAsciiDigit(c)))
        {
            return "ارقام پلاک باید فقط عدد باشند.";
        }

        if (!NormalLetters.Contains(letter) && letter is not ("الف" or "D" or "S"))
        {
            return "حرف پلاک نامعتبر است.";
        }

        return null;
    }
}
