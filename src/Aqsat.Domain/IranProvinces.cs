namespace Aqsat.Domain;

/// <summary>
/// The canonical 31-province list (post-2021 Alborz/Qom split, no DST). Single source of truth for
/// validation on <see cref="Organization.Province"/> and for the owner UI dropdown, so 60k agency
/// rows can never fragment into free-text variants of the same province.
/// </summary>
public static class IranProvinces
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "آذربایجان شرقی", "آذربایجان غربی", "اردبیل", "اصفهان", "البرز", "ایلام", "بوشهر",
        "تهران", "چهارمحال و بختیاری", "خراسان جنوبی", "خراسان رضوی", "خراسان شمالی",
        "خوزستان", "زنجان", "سمنان", "سیستان و بلوچستان", "فارس", "قزوین", "قم", "کردستان",
        "کرمان", "کرمانشاه", "کهگیلویه و بویراحمد", "گلستان", "گیلان", "لرستان", "مازندران",
        "مرکزی", "هرمزگان", "همدان", "یزد",
    };
}
