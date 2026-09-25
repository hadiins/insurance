using Aqsat.Domain.Enums;

namespace Aqsat.Api.Contracts;

/// <summary>One contract for every operator-recorded receipt. The structured method is the
/// authority; the free-text display label and destination ids must agree with it. This is kept
/// out of the controllers so payment, down-payment and full-policy receipts cannot drift.</summary>
public static class OperatorPaymentValidator
{
    public static string? Validate(
        string method, PaymentMethod methodType, Guid? cashBoxId, Guid? bankAccountId, object? cheque)
    {
        var expected = methodType switch
        {
            PaymentMethod.Cash => "نقدی",
            PaymentMethod.BankTransfer => "واریز بانکی",
            PaymentMethod.Cheque => "چک",
            PaymentMethod.PosDirect => "پوز مستقیم بیمه‌گر",
            _ => null,
        };
        if (expected is null)
        {
            return "روش پرداخت ساختاریافته فقط توسط سیستم مجاز است.";
        }
        if (method != expected)
        {
            return "عنوان و نوع ساختاریافته روش پرداخت باید یکسان باشند.";
        }

        return methodType switch
        {
            PaymentMethod.Cash when cashBoxId is null || bankAccountId is not null => "برای پرداخت نقدی فقط صندوق انتخاب می‌شود.",
            PaymentMethod.BankTransfer when bankAccountId is null || cashBoxId is not null => "برای واریز بانکی فقط حساب بانکی انتخاب می‌شود.",
            PaymentMethod.Cheque when cashBoxId is null || bankAccountId is not null || cheque is null => "برای پرداخت چکی، صندوق و مشخصات چک الزامی است.",
            PaymentMethod.PosDirect when cashBoxId is not null || bankAccountId is not null => "پوز مستقیم بیمه‌گر نباید صندوق یا حساب بانکی داشته باشد.",
            _ => null,
        };
    }
}
