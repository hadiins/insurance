namespace Aqsat.Application.Schedule;

/// <summary>
/// Blocked on api.ir access (docs/PHASE-1-SPEC.md §1) — define the interface now, implement for
/// real in Task 12/13. The default implementation (Aqsat.Infrastructure.Schedule.
/// WeekendOnlyHolidayChecker) only knows Friday; official calendar holidays aren't recognized
/// until the real IsHoliday service is wired in, cached until midnight per rule 26.
/// </summary>
public interface IHolidayChecker
{
    bool IsHoliday(DateOnly date);
}
