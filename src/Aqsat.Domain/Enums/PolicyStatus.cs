namespace Aqsat.Domain.Enums;

public enum PolicyStatus : byte
{
    Active = 1,
    Settled = 2,
    Cancelled = 3,

    /// <summary>The agent issued it but hasn't gotten the customer's sign-off yet — an internal
    /// worklist flag the agent sets/clears, not a customer-facing portal (CLAUDE.md's "customer
    /// self-registration portal" exclusion is a different, unbuilt feature).</summary>
    PendingConfirmation = 4,
}
