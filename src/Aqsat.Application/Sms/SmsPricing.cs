namespace Aqsat.Application.Sms;

/// <summary>The per-message SMS cost used everywhere a toman figure must be shown or confirmed
/// before money moves — preview cost (SmsController) and the effectiveness report's estimated
/// campaign cost. One constant, because two would drift.</summary>
public static class SmsPricing
{
    public const decimal PerMessageToman = 115m;
}
