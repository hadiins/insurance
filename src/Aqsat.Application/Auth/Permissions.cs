namespace Aqsat.Application.Auth;

/// <summary>The exact Phase-1 permission list from docs/PHASE-1-SPEC.md §2 — no magic strings.</summary>
public static class Permissions
{
    public const string PolicyRead = "Policy.Read";
    public const string PolicyWrite = "Policy.Write";
    public const string PaymentWrite = "Payment.Write";
    public const string ImportRun = "Import.Run";
    public const string SettingsWrite = "Settings.Write";
    public const string LockForceRelease = "Lock.ForceRelease";
    public const string ReportRead = "Report.Read";
    public const string FinanceRead = "Finance.Read";
    public const string MarketerManage = "Marketer.Manage";
    public const string MarketerSelfView = "Marketer.SelfView";
    public const string RiskNetworkRead = "Risk.NetworkRead";

    /// <summary>docs/UPDATE-SYSTEM.md rule 1: "belongs to you, not any agency." Granted via a Role
    /// assigned at the Headquarters organization — never seeded for an agency's own staff, and
    /// agency users never even see the update panel exists (rule 2: no read access, not even
    /// read-only).</summary>
    public const string PlatformOwner = "Platform.Owner";

    public static readonly IReadOnlyList<string> All =
    [
        PolicyRead, PolicyWrite, PaymentWrite, ImportRun, SettingsWrite, LockForceRelease, ReportRead,
        FinanceRead, MarketerManage, MarketerSelfView, PlatformOwner, RiskNetworkRead,
    ];
}
