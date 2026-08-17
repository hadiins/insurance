namespace Aqsat.Application.Import;

/// <summary>
/// Column-mapping target keys for the Fanavaran policy report. Distinct from
/// ImportTargetFields.All (Task 6's generic set) because the raw source combines insured name and
/// customer code into one column and needs its own contract-name/duration handling — the agency
/// still saves a mapping (reusing the same ImportColumnMapping mechanism, under ImportType
/// "FanavaranPolicyReport") since the real file's exact header text isn't something this codebase
/// can hardcode without ever having seen a real export.
/// </summary>
public static class FanavaranImportFields
{
    public const string ImportType = "FanavaranPolicyReport";
    public const string SheetName = "CarSalesBNVer";

    public const string PolicyNumber = "PolicyNumber";
    public const string InsuredNameAndCode = "InsuredNameAndCode";
    public const string Plate = "Plate";
    public const string Vin = "Vin";
    public const string Chassis = "Chassis";
    public const string IssueDate = "IssueDate";
    public const string StartDate = "StartDate";

    /// <summary>Duration in months — spec §4.1 lists "issue date, start date, duration" as the
    /// three needed date-adjacent fields; EndDate is computed, not itself a source column.</summary>
    public const string DurationMonths = "DurationMonths";

    /// <summary>Rials, always — §4.1: "Total premium with tax: RIALS → ÷10", stated as a fact of
    /// this specific export, not an operator choice like Task 6's generic amountsAreInRials flag.</summary>
    public const string TotalPremiumWithTaxRials = "TotalPremiumWithTaxRials";

    public const string ContractName = "ContractName";

    public static readonly IReadOnlyList<ImportTargetField> All =
    [
        new(PolicyNumber, "شمارهٔ بیمه‌نامه", Required: true, ImportFieldType.Text),
        new(InsuredNameAndCode, "نام و کد بیمه‌گذار", Required: true, ImportFieldType.Text),
        new(Plate, "پلاک", Required: false, ImportFieldType.Text),
        new(Vin, "شماره بدنه (VIN)", Required: false, ImportFieldType.Text),
        new(Chassis, "شماره شاسی", Required: false, ImportFieldType.Text),
        new(IssueDate, "تاریخ صدور", Required: true, ImportFieldType.Date),
        new(StartDate, "تاریخ شروع", Required: true, ImportFieldType.Date),
        new(DurationMonths, "مدت (ماه)", Required: false, ImportFieldType.Text),
        new(TotalPremiumWithTaxRials, "حق بیمه با عوارض (ریال)", Required: true, ImportFieldType.Money),
        new(ContractName, "نام قرارداد", Required: true, ImportFieldType.Text),
    ];
}
