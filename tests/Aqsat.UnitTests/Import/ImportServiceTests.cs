using Aqsat.Application.Common;
using Aqsat.Application.Import;
using Aqsat.Infrastructure.Import;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Security;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aqsat.UnitTests.Import;

/// <summary>
/// Runs against a real local SQL Server database, same as RowLevelSecurityTests/AuthenticationTests
/// — proves the generic import pipeline (Task 6) end to end, not a mocked persistence layer.
/// </summary>
public class ImportServiceTests
{
    private static readonly string[] Headers =
    [
        "شماره بیمه نامه", "کد بیمه گذار", "نام بیمه گذار",
        "تاریخ صدور", "تاریخ شروع", "تاریخ پایان", "حق بیمه", "قرارداد",
    ];

    private static byte[] BuildWorkbook(
        IReadOnlyList<(string PolicyNumber, string ExternalCode, string FullName, string IssueDate, decimal Premium, string ContractName)> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Policies");

        for (var i = 0; i < Headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = Headers[i];
        }

        var rowNumber = 2;
        foreach (var row in rows)
        {
            sheet.Cell(rowNumber, 1).Value = row.PolicyNumber;
            sheet.Cell(rowNumber, 2).Value = row.ExternalCode;
            sheet.Cell(rowNumber, 3).Value = row.FullName;
            sheet.Cell(rowNumber, 4).Value = row.IssueDate;
            sheet.Cell(rowNumber, 5).Value = row.IssueDate;
            sheet.Cell(rowNumber, 6).Value = "1405-05-01";
            sheet.Cell(rowNumber, 7).Value = row.Premium.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sheet.Cell(rowNumber, 8).Value = row.ContractName;
            rowNumber++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static Dictionary<string, string> BuildMapping() => new()
    {
        [ImportTargetFields.PolicyNumber] = Headers[0],
        [ImportTargetFields.CustomerExternalCode] = Headers[1],
        [ImportTargetFields.CustomerFullName] = Headers[2],
        [ImportTargetFields.IssueDate] = Headers[3],
        [ImportTargetFields.StartDate] = Headers[4],
        [ImportTargetFields.EndDate] = Headers[5],
        [ImportTargetFields.TotalPremium] = Headers[6],
        [ImportTargetFields.ContractName] = Headers[7],
    };

    private static IFieldEncryptor BuildFieldEncryptor()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:NationalIdKey"] = "iNR6AVHkisOPGbBreM0PpHSNmUoom7d0EFVWgcwEdJk=",
            })
            .Build();
        return new AesFieldEncryptor(configuration);
    }

    [Fact]
    public async Task Committing_the_same_file_twice_reports_100_percent_duplicates_and_creates_nothing_new()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var fileBytes = BuildWorkbook([
            ($"POL-{suffix}-1", $"EXT-{suffix}-1", "مشتری یک", "1404-05-01", 9_000_000m, "تجارت آفرینان تسنیم"),
            ($"POL-{suffix}-2", $"EXT-{suffix}-2", "مشتری دو", "1404-05-01", 5_000_000m, "تجارت آفرینان تسنیم"),
        ]);

        var service = new ImportService(context, new ClosedXmlWorkbookReader(), BuildFieldEncryptor());
        var mapping = BuildMapping();

        var firstRun = await service.CommitAsync(
            fileBytes, "policies.xlsx", agencyA.AgencyId, mapping, DetectedDateFormat.Jalali, amountsAreInRials: false, CancellationToken.None);

        Assert.Equal(2, firstRun.NewCount);
        Assert.Equal(0, firstRun.DuplicateCount);
        Assert.Equal(0, firstRun.FailedCount);

        var secondRun = await service.CommitAsync(
            fileBytes, "policies.xlsx", agencyA.AgencyId, mapping, DetectedDateFormat.Jalali, amountsAreInRials: false, CancellationToken.None);

        Assert.Equal(0, secondRun.NewCount);
        Assert.Equal(2, secondRun.DuplicateCount);
        Assert.Equal(0, secondRun.FailedCount);

        var policyCount = await context.Policies.AsNoTracking()
            .CountAsync(p => p.PolicyNumber.StartsWith($"POL-{suffix}"));
        Assert.Equal(2, policyCount);
    }

    [Fact]
    public async Task A_row_with_an_invalid_date_fails_without_aborting_the_rest_of_the_batch()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var fileBytes = BuildWorkbook([
            ($"POL-{suffix}-1", $"EXT-{suffix}-1", "مشتری یک", "1404-05-01", 9_000_000m, "تجارت آفرینان تسنیم"),
            ($"POL-{suffix}-2", $"EXT-{suffix}-2", "مشتری دو", "این-تاریخ-نیست", 5_000_000m, "تجارت آفرینان تسنیم"),
            ($"POL-{suffix}-3", $"EXT-{suffix}-3", "مشتری سه", "1404-05-01", 4_000_000m, "تجارت آفرینان تسنیم"),
        ]);

        var service = new ImportService(context, new ClosedXmlWorkbookReader(), BuildFieldEncryptor());

        var report = await service.CommitAsync(
            fileBytes, "policies.xlsx", agencyA.AgencyId, BuildMapping(), DetectedDateFormat.Jalali, amountsAreInRials: false, CancellationToken.None);

        Assert.Equal(2, report.NewCount);
        Assert.Equal(1, report.FailedCount);
        Assert.Equal(0, report.DuplicateCount);

        var policyCount = await context.Policies.AsNoTracking()
            .CountAsync(p => p.PolicyNumber.StartsWith($"POL-{suffix}"));
        Assert.Equal(2, policyCount);
    }
}
