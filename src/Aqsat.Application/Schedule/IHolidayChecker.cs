namespace Aqsat.Application.Schedule;

/// <summary>
/// Blocked on api.ir access (docs/PHASE-1-SPEC.md §1) — define the interface now, implement for
/// real in Task 12/13. The default implementation (Aqsat.Infrastructure.Schedule.
/// WeekendOnlyHolidayChecker) only knows Friday; official calendar holidays aren't recognized
/// until the real IsHoliday service is wired in, cached until midnight per rule 26.
///
/// Async, deliberately — the real implementation makes a network call. A synchronous signature
/// here previously forced Aqsat.Infrastructure.ApiIr.ApiIrHolidayChecker to block a thread on
/// `.GetAwaiter().GetResult()` for every single distinct deadline date, which under a large
/// installment backlog (e.g. DeadlineRecalculationJob iterating every agency) could tie up worker
/// threads for hours and starve unrelated requests — including login.
/// </summary>
public interface IHolidayChecker
{
    Task<bool> IsHolidayAsync(DateOnly date, CancellationToken ct = default);
}
