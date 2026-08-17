using Aqsat.Application.Schedule;

namespace Aqsat.Infrastructure.Schedule;

/// <summary>Placeholder IHolidayChecker (see the interface's doc comment) — Friday only, no
/// official calendar holidays until Task 12/13 wires the real api.ir IsHoliday service.</summary>
public sealed class WeekendOnlyHolidayChecker : IHolidayChecker
{
    public bool IsHoliday(DateOnly date) => date.DayOfWeek == DayOfWeek.Friday;
}
