using Aqsat.Application.ApiIr;
using Aqsat.Application.Schedule;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Schedule;

namespace Aqsat.Infrastructure.ApiIr;

/// <summary>
/// docs/TASKS.md Task 14 — replaces WeekendOnlyHolidayChecker as the registered IHolidayChecker.
/// IHolidayChecker.IsHoliday is synchronous (every call site — PoliciesController,
/// InstallmentsController, DeadlineRecalculationJob — expects that), so this blocks on the async
/// api.ir call rather than rippling an async signature change through all of them; the per-date
/// midnight cache in ApiIrClient means this only actually blocks once per calendar date. Falls back
/// to Friday-only whenever no real answer is available (sandboxed by default, or a genuine outage) —
/// docs/TASKS.md Task 12's "an api.ir outage degrades gracefully" check, applied here too.
/// </summary>
public sealed class ApiIrHolidayChecker(IApiIrClient client) : IHolidayChecker
{
    private static readonly WeekendOnlyHolidayChecker Fallback = new();

    public bool IsHoliday(DateOnly date)
    {
        var agencyId = AgencyContext.Current ?? Guid.Empty;
        var result = client.IsHolidayAsync(date, agencyId).GetAwaiter().GetResult();
        return result ?? Fallback.IsHoliday(date);
    }
}
