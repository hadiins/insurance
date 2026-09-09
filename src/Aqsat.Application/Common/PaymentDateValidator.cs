namespace Aqsat.Application.Common;

/// <summary>
/// Shared window for every money-receipt endpoint's PaidOn: a future date or one more than two
/// years in the past is always an entry mistake (most often a Jalali/Gregorian year mixup), and it
/// silently corrupts the cash-basis and aging reports — the payment lands in a period nobody ever
/// looks at. Reject it with a message that names the mistake instead.
/// </summary>
public static class PaymentDateValidator
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static bool IsValid(DateOnly paidOn) => paidOn <= Today && paidOn >= Today.AddYears(-2);

    public const string ErrorMessage =
        "تاریخ پرداخت نمی‌تواند در آینده یا بیش از ۲ سال گذشته باشد — در صورت تبدیل دستی تاریخ شمسی، سال را بازبینی کنید.";
}
