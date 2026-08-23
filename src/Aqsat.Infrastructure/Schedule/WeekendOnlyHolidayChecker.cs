using Aqsat.Application.Schedule;

namespace Aqsat.Infrastructure.Schedule;

/// <summary>Placeholder IHolidayChecker (see the interface's doc comment) — Friday only, no
/// official calendar holidays until Task 12/13 wires the real api.ir IsHoliday service.</summary>
public sealed class WeekendOnlyHolidayChecker : IHolidayChecker
{
    public Task<bool> IsHolidayAsync(DateOnly date, CancellationToken ct = default) =>
        Task.FromResult(date.DayOfWeek == DayOfWeek.Friday);
}
