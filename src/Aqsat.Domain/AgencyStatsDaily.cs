using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// Daily per-agency rollup for the owner platform (پروندهٔ نمایندگی): policies issued, SMS sent
/// and their api.ir cost, inquiry-fee payments (owner revenue) and actual api.ir inquiry calls.
/// Derived, rebuildable data populated by AgencyStatsRollupJob — unlike every AgencyOwnedEntity,
/// this table is deliberately NOT in the RLS security policy: the owner's session resolves to HQ,
/// so an RLS predicate here would make every cross-agency dashboard silently return zeros
/// (CLAUDE.md rule 17). Reads happen only from Platform.Owner-gated controllers.
/// </summary>
public class AgencyStatsDaily : SoftDeletableEntity
{
    public Guid AgencyId { get; set; }
    public Organization Agency { get; set; } = default!;

    /// <summary>Business day in Iran time (+3:30, no DST since 2022) the row aggregates.</summary>
    public DateOnly StatDate { get; set; }

    public int PoliciesIssued { get; set; }

    /// <summary>Count of api.ir SendSms/SmsOTP calls — the provider-level truth for SMS volume
    /// (ReminderLog only covers installment reminders), and the cost-bearing table.</summary>
    public int SmsSentCount { get; set; }
    public decimal SmsCostToman { get; set; }

    /// <summary>Paid portal invitations: the owner's inquiry-fee revenue events.</summary>
    public int InquiryPaymentsCount { get; set; }
    public decimal InquiryRevenueToman { get; set; }

    /// <summary>Actual api.ir inquiry executions (ShahkarLite, ChequeColor).</summary>
    public int InquiryCallsCount { get; set; }
    public decimal InquiryCallCostToman { get; set; }
}
