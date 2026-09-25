namespace Aqsat.Application.Common;

/// <summary>
/// The boundary between Iranian civil days. Iran abolished DST in 2022, so a fixed +3:30 offset is
/// exact. Every civil date in this system (DueDate, PaidOn, SettlementDeadline) is an Iranian day,
/// so "today" must never be the raw UTC date: between 00:00 and 03:30 Iran time those two disagree,
/// which shows a day-old dashboard, rejects a payment dated today as "in the future", and shifts
/// every countdown window by one day.
/// </summary>
public static class IranClock
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3.5);

    public static DateOnly Today() => Today(TimeProvider.System);

    public static DateOnly Today(TimeProvider timeProvider) => DayOf(timeProvider.GetUtcNow());

    public static DateOnly DayOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(Offset).DateTime);
}
