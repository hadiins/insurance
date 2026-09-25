namespace Aqsat.Infrastructure.Payments;

/// <summary>
/// Both PSPs take Amount as a JSON integer in toman. This system stores toman with no fractions
/// (decimal(18,0) everywhere — rule 19), so the conversion is normally a no-op — but a fractional
/// value reaching a PSP would be silently truncated by the far side and capture LESS money than the
/// customer owes. Refusing is the only safe outcome, and it is checked here once rather than being
/// remembered at each call site.
/// </summary>
internal static class PspAmount
{
    public static bool TryToWholeToman(decimal amountToman, out long whole)
    {
        whole = 0;

        if (amountToman <= 0 || amountToman > long.MaxValue)
        {
            return false;
        }

        if (decimal.Truncate(amountToman) != amountToman)
        {
            return false;
        }

        whole = (long)amountToman;
        return true;
    }
}
