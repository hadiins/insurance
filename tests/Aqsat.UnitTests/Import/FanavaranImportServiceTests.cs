using Aqsat.Application.Common;
using Aqsat.Application.Import;
using Aqsat.Domain;
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
/// Task 7's own check (docs/TASKS.md): "with a fixture of 180 rows and templates configured, 137
/// policies (76%) are flagged installment. Not 28. If you get 28, you searched for «اقساطی»."
/// Mirrors the real sample's distribution: 60.6% تجارت آفرینان تسنیم (109 rows) + 15.6% literal
/// اقساطی (28 rows) = 137 installment; the remaining 43 are 12.8%-ish نقدی (23) and blank (20).
/// </summary>
public class FanavaranImportServiceTests
{
    private static readonly string[] Headers =
    [
        "شماره بیمه نامه", "نام و کد بیمه گذار", "پلاک",
        "تاریخ صدور", "تاریخ شروع", "حق بیمه با عوارض", "نام قرارداد",
    ];

    private static byte[] BuildFanavaranWorkbook(IReadOnlyList<(string PolicyNumber, string ExternalCode, string ContractName)> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(FanavaranImportFields.SheetName);

        for (var i = 0; i < Headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = Headers[i];
        }

        var rowNumber = 2;
        foreach (var row in rows)
        {
            sheet.Cell(rowNumber, 1).Value = row.PolicyNumber;
            sheet.Cell(rowNumber, 2).Value = $"مشتری آزمایشی کد {row.ExternalCode}";
            sheet.Cell(rowNumber, 3).Value = "11الف111";
            sheet.Cell(rowNumber, 4).Value = "1404/05/01";
            sheet.Cell(rowNumber, 5).Value = "1404/05/01";
            sheet.Cell(rowNumber, 6).Value = "90000000"; // rials; /10 -> 9,000,000 toman
            // Real rows carry a "شماره قرارداد ..." suffix the parser must strip before matching.
            sheet.Cell(rowNumber, 7).Value = row.ContractName.Length == 0
                ? string.Empty
                : $"{row.ContractName} شماره قرارداد {rowNumber}";
            rowNumber++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static Dictionary<string, string> BuildMapping() => new()
    {
        [FanavaranImportFields.PolicyNumber] = Headers[0],
        [FanavaranImportFields.InsuredNameAndCode] = Headers[1],
        [FanavaranImportFields.Plate] = Headers[2],
        [FanavaranImportFields.IssueDate] = Headers[3],
        [FanavaranImportFields.StartDate] = Headers[4],
        [FanavaranImportFields.TotalPremiumWithTaxRials] = Headers[5],
        [FanavaranImportFields.ContractName] = Headers[6],
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
    public async Task A_180_row_fixture_flags_exactly_137_policies_as_installment_not_28()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;

        // The 80% problem: an agency configures templates by counterparty contract name, not just
        // the literal «اقساطی» label.
        context.ContractTemplates.AddRange(
            new ContractTemplate { AgencyId = agencyA.AgencyId, ContractNamePattern = "تجارت آفرینان تسنیم", IsInstallment = true, DefaultInstallmentCount = 9 },
            new ContractTemplate { AgencyId = agencyA.AgencyId, ContractNamePattern = "اقساطی", IsInstallment = true, DefaultInstallmentCount = 6 },
            new ContractTemplate { AgencyId = agencyA.AgencyId, ContractNamePattern = "نقدی", IsInstallment = false, DefaultInstallmentCount = 0 });
        await context.SaveChangesAsync();

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var rows = new List<(string PolicyNumber, string ExternalCode, string ContractName)>();
        var n = 0;
        for (var i = 0; i < 109; i++) rows.Add(($"POL-{suffix}-{n}", $"{suffix}-{n++}", "تجارت آفرینان تسنیم"));
        for (var i = 0; i < 28; i++) rows.Add(($"POL-{suffix}-{n}", $"{suffix}-{n++}", "اقساطی"));
        for (var i = 0; i < 23; i++) rows.Add(($"POL-{suffix}-{n}", $"{suffix}-{n++}", "نقدی"));
        for (var i = 0; i < 20; i++) rows.Add(($"POL-{suffix}-{n}", $"{suffix}-{n++}", ""));
        Assert.Equal(180, rows.Count);

        var fileBytes = BuildFanavaranWorkbook(rows);

        var service = new ImportService(context, new ClosedXmlWorkbookReader(), BuildFieldEncryptor());
        await service.SaveMappingAsync(FanavaranImportFields.ImportType, agencyA.AgencyId, BuildMapping(), CancellationToken.None);

        var report = await service.CommitFanavaranPolicyReportAsync(
            fileBytes, "policy-report.xlsx", agencyA.AgencyId, CancellationToken.None);

        Assert.Equal(180, report.NewCount);
        Assert.Equal(0, report.FailedCount);
        Assert.Equal(0, report.DuplicateCount);

        var installmentCount = await context.Policies.AsNoTracking()
            .CountAsync(p => p.PolicyNumber.StartsWith($"POL-{suffix}") && p.IsInstallment);

        Assert.Equal(137, installmentCount);
        Assert.NotEqual(28, installmentCount);
    }

    [Fact]
    public async Task Insured_name_and_code_and_contract_number_suffix_are_parsed_correctly()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;

        context.ContractTemplates.Add(new ContractTemplate
        {
            AgencyId = agencyA.AgencyId,
            ContractNamePattern = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            DefaultInstallmentCount = 9,
        });
        await context.SaveChangesAsync();

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var fileBytes = BuildFanavaranWorkbook([($"POL-{suffix}", $"{suffix}-X", "تجارت آفرینان تسنیم")]);

        var service = new ImportService(context, new ClosedXmlWorkbookReader(), BuildFieldEncryptor());
        await service.SaveMappingAsync(FanavaranImportFields.ImportType, agencyA.AgencyId, BuildMapping(), CancellationToken.None);

        var report = await service.CommitFanavaranPolicyReportAsync(
            fileBytes, "policy-report.xlsx", agencyA.AgencyId, CancellationToken.None);

        Assert.Equal(1, report.NewCount);

        var policy = await context.Policies.AsNoTracking()
            .Include(p => p.Customer)
            .SingleAsync(p => p.PolicyNumber == $"POL-{suffix}");

        Assert.Equal($"{suffix}-X", policy.Customer.ExternalCode);
        Assert.Equal("مشتری آزمایشی", policy.Customer.FullName);
        Assert.Equal("تجارت آفرینان تسنیم", policy.ContractName);
        Assert.True(policy.IsInstallment);
        Assert.Equal(9_000_000m, policy.TotalPremium);
    }
}
