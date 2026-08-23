using Aqsat.Application.ApiIr;
using Aqsat.Application.Schedule;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Schedule;

namespace Aqsat.Infrastructure.ApiIr;

/// <summary>
/// docs/TASKS.md Task 14 — replaces WeekendOnlyHolidayChecker as the registered IHolidayChecker.
/// Falls back to Friday-only whenever no real answer is available (sandboxed by default, or a
/// genuine outage) — docs/TASKS.md Task 12's "an api.ir outage degrades gracefully" check, applied
/// here too.
/// </summary>
public sealed class ApiIrHolidayChecker(IApiIrClient client) : IHolidayChecker
{
    private static readonly WeekendOnlyHolidayChecker Fallback = new();

    public async Task<bool> IsHolidayAsync(DateOnly date, CancellationToken ct = default)
    {
        var agencyId = AgencyContext.Current ?? Guid.Empty;
        var result = await client.IsHolidayAsync(date, agencyId, ct);
        return result ?? await Fallback.IsHolidayAsync(date, ct);
    }
}
