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

    public static readonly IReadOnlyList<string> All =
    [
        PolicyRead, PolicyWrite, PaymentWrite, ImportRun, SettingsWrite, LockForceRelease, ReportRead,
        FinanceRead, MarketerManage, MarketerSelfView,
    ];
}
