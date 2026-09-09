using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// One agency's shared, status-only view of a person's credit risk (owner decision 2026-09-07:
/// the cross-agency lookup other agencies see when they search a national ID or plate). Upserted
/// by the assessment pipeline in the same transaction as the RiskAssessment itself — one live row
/// per (source agency, NationalIdHash). Deliberately NOT in the RLS security policy (same
/// exemption as AgencyStatsDaily): cross-agency reads are the whole point, gated by the platform
/// owner's RiskNetworkSettings switch and the Risk.NetworkRead permission instead.
/// Status only — never a monetary amount, never a name/mobile/policy number (owner decision:
/// «فقط وضعیت»). The person is identified solely by the keyed HMAC NationalIdHash, which is
/// stable across agencies because the HMAC key is platform-wide.
/// </summary>
public class NetworkRiskProfile : SoftDeletableEntity
{
    public Guid AgencyId { get; set; }
    public Organization Agency { get; set; } = default!;

    /// <summary>The source agency's own customer row that produced this profile — an internal
    /// traceability reference, never exposed through the network lookup API.</summary>
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = default!;

    /// <summary>HMAC-SHA256 of the national ID (IFieldEncryptor.Hash) — the cross-agency match
    /// key. The plaintext national ID is never stored here.</summary>
    public byte[] NationalIdHash { get; set; } = default!;

    public int Score { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public RiskDecision Decision { get; set; }

    public int OverdueCount { get; set; }
    public int MaxDaysOverdue { get; set; }
    public int ReturnedChequeCount { get; set; }
    public decimal OnTimeRatePercent { get; set; }
    public int TenureMonths { get; set; }
    public int SettledInstallmentCount { get; set; }

    public DateTimeOffset CalculatedAt { get; set; }
}
