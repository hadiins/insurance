namespace Aqsat.Api.Contracts;

/// <summary>
/// Shared guard for every cheque-receipt path (standalone payment, full payment, down payment):
/// the same entry mistakes the issuance guards catch — an empty cheque number, a forgotten cash
/// box, a DueDate born from a Jalali/Gregorian year mixup — otherwise surface as a raw 500
/// (NullReferenceException on Trim, FK violation on Guid.Empty) instead of a Persian reason.
/// </summary>
public static class ChequeDetailsValidator
{
    public static string? Validate(ChequeDetailsRequest cheque)
    {
        if (string.IsNullOrWhiteSpace(cheque.ChequeNumber) || cheque.ChequeNumber.Trim().Length > 50)
        {
            return "شمارهٔ چک الزامی است (حداکثر ۵۰ کاراکتر).";
        }

        if (string.IsNullOrWhiteSpace(cheque.BankName))
        {
            return "نام بانک چک الزامی است.";
        }

        if (string.IsNullOrWhiteSpace(cheque.PresenterName))
        {
            return "نام صادرکنندهٔ چک الزامی است.";
        }

        if (cheque.CashBoxId == Guid.Empty)
        {
            return "صندوق محل دریافت چک انتخاب نشده است.";
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (cheque.DueDate < today.AddYears(-1) || cheque.DueDate > today.AddYears(3))
        {
            return "تاریخ سررسید چک باید در بازهٔ یک سال گذشته تا سه سال آینده باشد — در صورت تبدیل دستی تاریخ شمسی، سال را بازبینی کنید.";
        }

        return null;
    }
}
