namespace Aqsat.Application.Schedule;

/// <summary>
/// Whether a given date is an official holiday — the settlement deadline shifts past it, the due date
/// itself never moves. The registered implementation is
/// Aqsat.Infrastructure.Schedule.WeekendOnlyHolidayChecker: Friday only, no network call.
///
/// Owner decision 2026-09-23 (docs/PHASE-1-SPEC.md §6) parked the paid api.ir IsHoliday lookup (150
/// Toman per distinct date, cached to midnight per rule 26) because the account does not carry that
/// service. IApiIrClient.IsHolidayAsync stays implemented and tested, so bringing it back is the thin
/// adapter plus one registration line — never a schema change.
///
/// Async, deliberately — the paid implementation makes a network call. A synchronous signature here
/// previously forced a `.GetAwaiter().GetResult()` per distinct date, which under a large installment
/// backlog (DeadlineRecalculationJob iterating every agency) could tie up worker threads for hours
/// and starve unrelated requests — including login.
/// </summary>
public interface IHolidayChecker
{
    Task<bool> IsHolidayAsync(DateOnly date, CancellationToken ct = default);
}
