using Aqsat.Application.Schedule;

namespace Aqsat.Infrastructure.Schedule;

/// <summary>The registered IHolidayChecker — Friday only, no network call, nothing billed. Owner
/// decision 2026-09-23 (docs/PHASE-1-SPEC.md §6) parked the paid api.ir IsHoliday lookup because the
/// account does not carry it, so an official holiday that falls on a non-Friday currently does not
/// delay a settlement deadline (the §3.1 shift still applies to Fridays). See DependencyInjection.cs
/// for how to switch the paid lookup back on; no caller knows which implementation it received.</summary>
public sealed class WeekendOnlyHolidayChecker : IHolidayChecker
{
    public Task<bool> IsHolidayAsync(DateOnly date, CancellationToken ct = default) =>
        Task.FromResult(date.DayOfWeek == DayOfWeek.Friday);
}
