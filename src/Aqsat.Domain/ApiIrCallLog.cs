using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// docs/TASKS.md Task 14 "cost logging" — every api.ir call, sandboxed or real, gets one row.
/// CLAUDE.md rule 25/26's whole point is that these costs compound fast; this is what makes the
/// number visible rather than discovered on an invoice.
/// </summary>
public class ApiIrCallLog : AgencyOwnedEntity
{
    public string Service { get; set; } = default!;
    public bool Success { get; set; }
    public decimal CostToman { get; set; }

    /// <summary>True when AllowPaidEndpoints was off and this call was routed to Sandbox/Echo
    /// instead of the real (billed) endpoint — CostToman is still the *would-be* cost, so the log
    /// stays useful for estimating real spend before flipping the flag.</summary>
    public bool WasSandboxed { get; set; }

    public DateTimeOffset CalledAt { get; set; }
}
