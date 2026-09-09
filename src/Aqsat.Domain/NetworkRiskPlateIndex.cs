using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// Maps a normalized vehicle plate (e.g. "55الف555-55") to the NationalIdHash of the customer
/// whose policy covered that vehicle at this agency — the plate search path of the cross-agency
/// risk lookup (owner decision 2026-09-07). Synced by the nightly RiskAssessmentJob; insert-only,
/// because plate→person is a historical fact: when a car changes hands, the previous owner's row
/// stays valid (the searching agency sees every owner the network knows about). Deliberately NOT
/// in the RLS security policy, like NetworkRiskProfile.
/// </summary>
public class NetworkRiskPlateIndex : SoftDeletableEntity
{
    public Guid AgencyId { get; set; }
    public Organization Agency { get; set; } = default!;

    public string PlateNormalized { get; set; } = default!;

    /// <summary>The policy-holding customer's keyed HMAC national-ID hash (see
    /// NetworkRiskProfile.NationalIdHash).</summary>
    public byte[] NationalIdHash { get; set; } = default!;

    public DateTimeOffset SyncedAt { get; set; }
}
