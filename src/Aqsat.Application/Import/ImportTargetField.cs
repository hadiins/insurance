namespace Aqsat.Application.Import;

public enum ImportFieldType
{
    Text,
    Date,
    Money,
}

public sealed record ImportTargetField(string Key, string Label, bool Required, ImportFieldType Type);

/// <summary>
/// The fixed set of fields the generic import pipeline (docs/PHASE-1-SPEC.md §4.3) can populate on
/// Customer/Vehicle/Policy. Deliberately excludes InstallmentCount, DownPayment and IsInstallment —
/// spec §4.1 is explicit that installment count is never in the source file and must come from a
/// template or manual entry (Task 8), and down payment is an amount the agent picks per policy, not
/// an import-time value.
/// </summary>
public static class ImportTargetFields
{
    public const string PolicyNumber = "PolicyNumber";
    public const string CustomerExternalCode = "CustomerExternalCode";
    public const string CustomerFullName = "CustomerFullName";
    public const string CustomerMobile = "CustomerMobile";
    public const string CustomerNationalId = "CustomerNationalId";
    public const string VehiclePlate = "VehiclePlate";
    public const string VehicleVin = "VehicleVin";
    public const string VehicleChassis = "VehicleChassis";
    public const string VehicleMake = "VehicleMake";
    public const string VehicleModel = "VehicleModel";
    public const string VehicleYear = "VehicleYear";
    public const string IssueDate = "IssueDate";
    public const string StartDate = "StartDate";
    public const string EndDate = "EndDate";
    public const string TotalPremium = "TotalPremium";
    public const string ContractName = "ContractName";

    public static readonly IReadOnlyList<ImportTargetField> All =
    [
        new(PolicyNumber, "شمارهٔ بیمه‌نامه", Required: true, ImportFieldType.Text),
        new(CustomerExternalCode, "کد بیمه‌گذار", Required: true, ImportFieldType.Text),
        new(CustomerFullName, "نام بیمه‌گذار", Required: true, ImportFieldType.Text),
        new(CustomerMobile, "موبایل بیمه‌گذار", Required: false, ImportFieldType.Text),
        new(CustomerNationalId, "کد ملی", Required: false, ImportFieldType.Text),
        new(VehiclePlate, "پلاک", Required: false, ImportFieldType.Text),
        new(VehicleVin, "شماره بدنه (VIN)", Required: false, ImportFieldType.Text),
        new(VehicleChassis, "شماره شاسی", Required: false, ImportFieldType.Text),
        new(VehicleMake, "برند خودرو", Required: false, ImportFieldType.Text),
        new(VehicleModel, "مدل خودرو", Required: false, ImportFieldType.Text),
        new(VehicleYear, "سال ساخت", Required: false, ImportFieldType.Text),
        new(IssueDate, "تاریخ صدور", Required: true, ImportFieldType.Date),
        new(StartDate, "تاریخ شروع", Required: true, ImportFieldType.Date),
        new(EndDate, "تاریخ پایان", Required: true, ImportFieldType.Date),
        new(TotalPremium, "حق بیمه کل", Required: true, ImportFieldType.Money),
        new(ContractName, "نام قرارداد", Required: true, ImportFieldType.Text),
    ];
}
