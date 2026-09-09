using System.Security.Cryptography;
using System.Text.Json;
using Aqsat.Application.Common;
using Aqsat.Application.Import;
using Aqsat.Application.Numbering;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Import;

/// <summary>
/// The generic import pipeline (docs/PHASE-1-SPEC.md §4.3, docs/TASKS.md Task 6): upload → preview
/// → column mapping → validate → commit → batch report. CommitFanavaranPolicyReportAsync (Task 7)
/// is a concrete adapter over the same per-row persistence core (ProcessRowAsync/RunCommitAsync) —
/// it only supplies its own sheet selection and row-parsing (name+code split, contract-name strip,
/// fixed rials conversion); everything else (dedupe, template matching, per-row failure isolation,
/// batch bookkeeping) is shared, not duplicated.
/// </summary>
public sealed class ImportService(AppDbContext dbContext, IWorkbookReader workbookReader, IFieldEncryptor fieldEncryptor)
{
    public const string PolicyReportImportType = "PolicyReport";

    public ImportPreview BuildPreview(byte[] fileBytes)
    {
        using var stream = new MemoryStream(fileBytes);
        var sheet = workbookReader.ReadFirstSheet(stream);
        var sampleRows = sheet.Rows.Take(20).ToList();

        var columns = new List<ImportPreviewColumn>(sheet.Headers.Count);
        for (var i = 0; i < sheet.Headers.Count; i++)
        {
            var columnIndex = i;
            var columnValues = sampleRows.Select(r => columnIndex < r.Count ? r[columnIndex] : string.Empty);
            columns.Add(new ImportPreviewColumn(
                sheet.Headers[i],
                ImportPreviewDetector.DetectDateFormat(columnValues),
                ImportPreviewDetector.DetectCurrencyScale(columnValues)));
        }

        return new ImportPreview(columns, sampleRows, ImportTargetFields.All);
    }

    public async Task<Dictionary<string, string>?> GetSavedMappingAsync(string importType, CancellationToken ct)
    {
        var saved = await dbContext.ImportColumnMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ImportType == importType, ct);
        return saved is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(saved.MappingJson);
    }

    public async Task SaveMappingAsync(string importType, Guid agencyId, Dictionary<string, string> mapping, CancellationToken ct)
    {
        var existing = await dbContext.ImportColumnMappings.FirstOrDefaultAsync(m => m.ImportType == importType, ct);
        var json = JsonSerializer.Serialize(mapping);

        if (existing is null)
        {
            dbContext.ImportColumnMappings.Add(new ImportColumnMapping
            {
                AgencyId = agencyId,
                ImportType = importType,
                MappingJson = json,
            });

            try
            {
                await dbContext.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException)
            {
                // Two open tabs saved this agency's mapping at once — the unique index
                // (AgencyId, ImportType) turned the read-then-insert race into this violation.
                // Last save wins, which is exactly what two tabs expect; only a re-read that
                // still finds nothing is a real failure (rule 15 — never swallow it).
                dbContext.ChangeTracker.Clear();
                existing = await dbContext.ImportColumnMappings.FirstOrDefaultAsync(m => m.ImportType == importType, ct);
                if (existing is null)
                {
                    throw;
                }
            }
        }

        existing.MappingJson = json;
        await dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// amountsAreInRials and dateFormat are explicit operator confirmations from the mapping step,
    /// never taken from BuildPreview's heuristic guess — CLAUDE.md rule 19 requires the unit be
    /// shown, not silently assumed, for anything touching money.
    /// </summary>
    public async Task<ImportCommitReport> CommitAsync(
        byte[] fileBytes,
        string fileName,
        Guid agencyId,
        Dictionary<string, string> mapping,
        DetectedDateFormat dateFormat,
        bool amountsAreInRials,
        CancellationToken ct)
    {
        using var stream = new MemoryStream(fileBytes);
        var sheet = workbookReader.ReadFirstSheet(stream);
        EnsureRequiredFieldsMapped(mapping, ImportTargetFields.All);
        var columnIndex = BuildColumnIndex(sheet.Headers, mapping);

        return await RunCommitAsync(
            agencyId,
            fileName,
            ComputeHash(fileBytes),
            sheet.Rows,
            row => ParseGenericRow(row, columnIndex, dateFormat, amountsAreInRials),
            ct);
    }

    /// <summary>
    /// Task 7: reads the fixed "CarSalesBNVer" sheet using whatever column mapping the agency has
    /// saved under FanavaranImportFields.ImportType (this codebase has never seen a real export, so
    /// exact header text can't be hardcoded — the mapping mechanism from Task 6 already solves
    /// that). Rials→toman is always applied (not operator-confirmed like the generic path) because
    /// §4.1 states the source format's unit as a fact, not a per-file choice.
    /// </summary>
    public async Task<ImportCommitReport> CommitFanavaranPolicyReportAsync(
        byte[] fileBytes, string fileName, Guid agencyId, CancellationToken ct)
    {
        var mapping = await GetSavedMappingAsync(FanavaranImportFields.ImportType, ct)
            ?? throw new InvalidOperationException("نگاشت ستون‌های گزارش فاناوران هنوز پیکربندی نشده است.");

        using var stream = new MemoryStream(fileBytes);
        var sheet = workbookReader.ReadSheet(stream, FanavaranImportFields.SheetName);
        EnsureRequiredFieldsMapped(mapping, FanavaranImportFields.All);
        var columnIndex = BuildColumnIndex(sheet.Headers, mapping);

        return await RunCommitAsync(
            agencyId,
            fileName,
            ComputeHash(fileBytes),
            sheet.Rows,
            row => ParseFanavaranRow(row, columnIndex),
            ct);
    }

    /// <summary>
    /// The import review's I6: an unmapped required target field used to surface as the same
    /// «... خالی است» failure on every single row — 500 failed rows instead of one clear Persian
    /// error before anything is read. Thrown as InvalidOperationException so the import
    /// controller's existing mapping turns it into a 400.
    /// </summary>
    private static void EnsureRequiredFieldsMapped(
        IReadOnlyDictionary<string, string> mapping, IReadOnlyList<ImportTargetField> fields)
    {
        var missing = fields
            .Where(f => f.Required
                && (!mapping.TryGetValue(f.Key, out var column) || string.IsNullOrWhiteSpace(column)))
            .Select(f => f.Label)
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"نگاشت ستون‌های الزامی انجام نشده است: {string.Join("، ", missing)}");
        }
    }

    private async Task<ImportCommitReport> RunCommitAsync(
        Guid agencyId,
        string fileName,
        string fileHash,
        IReadOnlyList<IReadOnlyList<string>> rows,
        Func<IReadOnlyList<string>, (ParsedPolicyRow? Row, string? Error)> parseRow,
        CancellationToken ct)
    {
        var templates = await dbContext.ContractTemplates.AsNoTracking().ToListAsync(ct);

        // Both import paths (generic and Fanavaran) are motor-insurance exports (Vehicle fields
        // present) — every imported policy is ثالث until a non-motor import source exists.
        var thirdPartyLineId = await dbContext.InsuranceLines.AsNoTracking()
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode)
            .Select(l => l.Id)
            .FirstOrDefaultAsync(ct);
        if (thirdPartyLineId == Guid.Empty)
        {
            throw new InvalidOperationException("رشتهٔ بیمهٔ «ثالث» هنوز پیکربندی نشده است.");
        }

        var batch = new ImportBatch { AgencyId = agencyId, FileName = fileName, FileHash = fileHash };
        dbContext.ImportBatches.Add(batch);
        await dbContext.SaveChangesAsync(ct);

        var newCount = 0;
        var duplicateCount = 0;
        var failedCount = 0;
        var rowRecords = new List<ImportRow>();

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowNumber = rowIndex + 1;
            var (parsed, parseError) = parseRow(rows[rowIndex]);
            if (parsed is null)
            {
                rowRecords.Add(NewRow(agencyId, batch.Id, rowNumber, ImportRowStatus.Failed, parseError));
                failedCount++;
                continue;
            }

            // docs/PHASE-1-SPEC.md §2 states the dedupe key as "PolicyNumber + SeqNo", but Policy
            // has no SeqNo — that field belongs to Installment (§3.5's cartable/reminder context).
            // Task 6/7 import policies, not installments (schedule generation is Task 8), so
            // PolicyNumber alone is the dedupe key here, matching the Check's own wording exactly
            // ("import the same file twice — second run reports 100% duplicates").
            var alreadyExists = await dbContext.Policies.AsNoTracking()
                .AnyAsync(p => p.PolicyNumber == parsed.PolicyNumber, ct);
            if (alreadyExists)
            {
                rowRecords.Add(NewRow(agencyId, batch.Id, rowNumber, ImportRowStatus.Duplicate));
                duplicateCount++;
                continue;
            }

            try
            {
                await CreatePolicyAsync(agencyId, batch.Id, thirdPartyLineId, parsed, templates, ct);
                rowRecords.Add(NewRow(agencyId, batch.Id, rowNumber, ImportRowStatus.New));
                newCount++;
            }
            catch (DbUpdateException)
            {
                // A row failing to persist must never abort the batch. Detach whatever this
                // attempt added so the tracker doesn't retry stale/invalid entities on the next
                // row's SaveChanges.
                foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                {
                    entry.State = EntityState.Detached;
                }

                rowRecords.Add(NewRow(agencyId, batch.Id, rowNumber, ImportRowStatus.Failed, "خطای پایگاه‌داده هنگام ذخیره‌سازی."));
                failedCount++;
            }
        }

        dbContext.ImportRows.AddRange(rowRecords);
        batch.NewCount = newCount;
        batch.DuplicateCount = duplicateCount;
        batch.FailedCount = failedCount;
        await dbContext.SaveChangesAsync(ct);

        return new ImportCommitReport(batch.Id, newCount, duplicateCount, failedCount);
    }

    private async Task CreatePolicyAsync(
        Guid agencyId, Guid batchId, Guid insuranceLineId, ParsedPolicyRow row, IReadOnlyList<ContractTemplate> templates,
        CancellationToken ct)
    {
        var customer = await dbContext.Customers.FirstOrDefaultAsync(c => c.ExternalCode == row.CustomerExternalCode, ct);
        if (customer is null)
        {
            customer = new Customer
            {
                AgencyId = agencyId,
                ExternalCode = row.CustomerExternalCode,
                FullName = row.CustomerFullName,
                Mobile = row.CustomerMobile,
            };

            if (row.CustomerNationalId is not null)
            {
                customer.NationalId = row.CustomerNationalId;
                customer.NationalIdHash = fieldEncryptor.Hash(row.CustomerNationalId);
            }

            dbContext.Customers.Add(customer);
        }

        var plateParts = PlateParser.Parse(row.VehiclePlate);
        var vehicle = new Vehicle
        {
            AgencyId = agencyId,
            Plate = row.VehiclePlate,
            Vin = row.VehicleVin,
            Chassis = row.VehicleChassis,
            Make = row.VehicleMake,
            Model = row.VehicleModel,
            Year = row.VehicleYear,
            PlateTwoDigit = plateParts.IsParsed ? plateParts.TwoDigit : null,
            PlateLetter = plateParts.IsParsed ? plateParts.Letter : null,
            PlateThreeDigit = plateParts.IsParsed ? plateParts.ThreeDigit : null,
            PlateIranCode = plateParts.IsParsed ? plateParts.IranCode : null,
        };
        // TASK-25 §5.4 — an unparseable plate never rejects the row; it just stays unindexed
        // (the nightly plate-index sync only reads PlateNormalized, same as manual issuance).
        vehicle.PlateNormalized = plateParts.IsParsed ? plateParts.Normalized : null;
        dbContext.Vehicles.Add(vehicle);

        // The 80% problem (CLAUDE.md, PHASE-1-SPEC §2): resolved via agency-configured
        // ContractTemplate matching, never a hardcoded "اقساطی" search. InstallmentCount stays 0
        // here — the actual schedule (count, per-installment amount) is Task 8's job; this only
        // resolves the boolean flag Task 7's own check requires.
        var matchedTemplate = ContractTemplateMatcher.Match(row.ContractName, templates);

        var policy = new Policy
        {
            AgencyId = agencyId,
            PolicyNumber = row.PolicyNumber,
            InsuranceLineId = insuranceLineId,
            Customer = customer,
            Vehicle = vehicle,
            ContractName = row.ContractName,
            IsInstallment = matchedTemplate?.IsInstallment ?? false,
            IssueDate = row.IssueDate,
            StartDate = row.StartDate,
            EndDate = row.EndDate,
            NetPremium = row.TotalPremium,
            DownPayment = 0,
            InstallmentCount = 0,
            ImportBatchId = batchId,
        };
        dbContext.Policies.Add(policy);

        await dbContext.SaveChangesAsync(ct);
    }

    private static Dictionary<string, int> BuildColumnIndex(IReadOnlyList<string> headers, Dictionary<string, string> mapping)
    {
        var columnIndex = new Dictionary<string, int>();
        foreach (var (targetKey, sourceHeader) in mapping)
        {
            var index = headers
                .Select((header, i) => (header, i))
                .Where(h => string.Equals(h.header.Trim(), sourceHeader.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(h => h.i)
                .DefaultIfEmpty(-1)
                .First();

            if (index >= 0)
            {
                columnIndex[targetKey] = index;
            }
        }

        return columnIndex;
    }

    private static string ComputeHash(byte[] fileBytes) => Convert.ToHexString(SHA256.HashData(fileBytes));

    private static ImportRow NewRow(Guid agencyId, Guid batchId, int rowNumber, ImportRowStatus status, string? error = null) =>
        new()
        {
            AgencyId = agencyId,
            ImportBatchId = batchId,
            RowNumber = rowNumber,
            Status = status,
            ErrorMessage = error,
        };

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static (ParsedPolicyRow? Row, string? Error) ParseGenericRow(
        IReadOnlyList<string> row, Dictionary<string, int> columnIndex, DetectedDateFormat dateFormat, bool amountsAreInRials)
    {
        string Get(string key) => columnIndex.TryGetValue(key, out var idx) && idx < row.Count ? row[idx].Trim() : string.Empty;

        var policyNumber = Get(ImportTargetFields.PolicyNumber);
        if (string.IsNullOrWhiteSpace(policyNumber))
        {
            return (null, "شمارهٔ بیمه‌نامه خالی است.");
        }

        var externalCode = Get(ImportTargetFields.CustomerExternalCode);
        if (string.IsNullOrWhiteSpace(externalCode))
        {
            return (null, "کد بیمه‌گذار خالی است.");
        }

        var fullName = Get(ImportTargetFields.CustomerFullName);
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (null, "نام بیمه‌گذار خالی است.");
        }

        if (!ImportDateParser.TryParse(Get(ImportTargetFields.IssueDate), dateFormat, out var issueDate))
        {
            return (null, "تاریخ صدور نامعتبر است.");
        }

        if (!ImportDateParser.TryParse(Get(ImportTargetFields.StartDate), dateFormat, out var startDate))
        {
            return (null, "تاریخ شروع نامعتبر است.");
        }

        if (!ImportDateParser.TryParse(Get(ImportTargetFields.EndDate), dateFormat, out var endDate))
        {
            return (null, "تاریخ پایان نامعتبر است.");
        }

        var premiumRaw = Get(ImportTargetFields.TotalPremium).Replace(",", "");
        if (!decimal.TryParse(premiumRaw, out var totalPremium) || totalPremium <= 0)
        {
            return (null, "حق بیمه نامعتبر است.");
        }

        var contractName = Get(ImportTargetFields.ContractName);
        if (string.IsNullOrWhiteSpace(contractName))
        {
            return (null, "نام قرارداد خالی است.");
        }

        var year = int.TryParse(Get(ImportTargetFields.VehicleYear), out var y) ? y : (int?)null;
        var premium = amountsAreInRials ? totalPremium / 10m : totalPremium;

        return (new ParsedPolicyRow(
            policyNumber, externalCode, fullName,
            NullIfEmpty(Get(ImportTargetFields.CustomerMobile)),
            NullIfEmpty(Get(ImportTargetFields.CustomerNationalId)),
            NullIfEmpty(Get(ImportTargetFields.VehiclePlate)),
            NullIfEmpty(Get(ImportTargetFields.VehicleVin)),
            NullIfEmpty(Get(ImportTargetFields.VehicleChassis)),
            NullIfEmpty(Get(ImportTargetFields.VehicleMake)),
            NullIfEmpty(Get(ImportTargetFields.VehicleModel)),
            year,
            issueDate, startDate, endDate, premium, contractName), null);
    }

    private static (ParsedPolicyRow? Row, string? Error) ParseFanavaranRow(
        IReadOnlyList<string> row, Dictionary<string, int> columnIndex)
    {
        string Get(string key) => columnIndex.TryGetValue(key, out var idx) && idx < row.Count ? row[idx].Trim() : string.Empty;

        var policyNumber = Get(FanavaranImportFields.PolicyNumber);
        if (string.IsNullOrWhiteSpace(policyNumber))
        {
            return (null, "شمارهٔ بیمه‌نامه خالی است.");
        }

        var nameAndCode = Get(FanavaranImportFields.InsuredNameAndCode);
        if (string.IsNullOrWhiteSpace(nameAndCode))
        {
            return (null, "نام و کد بیمه‌گذار خالی است.");
        }

        var (fullName, externalCode) = FanavaranFieldParsers.ParseInsuredNameAndCode(nameAndCode);
        if (externalCode is null)
        {
            return (null, "کد بیمه‌گذار از ستون نام/کد قابل استخراج نیست.");
        }

        if (!ImportDateParser.TryParse(Get(FanavaranImportFields.IssueDate), DetectedDateFormat.Jalali, out var issueDate))
        {
            return (null, "تاریخ صدور نامعتبر است.");
        }

        if (!ImportDateParser.TryParse(Get(FanavaranImportFields.StartDate), DetectedDateFormat.Jalali, out var startDate))
        {
            return (null, "تاریخ شروع نامعتبر است.");
        }

        var durationMonths = int.TryParse(Get(FanavaranImportFields.DurationMonths), out var d) && d > 0 ? d : 12;
        var endDate = startDate.AddMonths(durationMonths);

        var premiumRaw = Get(FanavaranImportFields.TotalPremiumWithTaxRials).Replace(",", "");
        if (!decimal.TryParse(premiumRaw, out var totalPremiumRials) || totalPremiumRials <= 0)
        {
            return (null, "حق بیمه نامعتبر است.");
        }

        // Unlike the generic path, a blank contract name is not a validation failure here — §4.1's
        // real sample distribution has ~9.4% blank contract names, and those are still legitimate
        // (non-installment) policies, not bad rows. An empty string simply never matches any
        // ContractTemplate in ContractTemplateMatcher.
        var contractName = FanavaranFieldParsers.StripContractNumber(Get(FanavaranImportFields.ContractName));

        return (new ParsedPolicyRow(
            policyNumber, externalCode, fullName,
            CustomerMobile: null,
            CustomerNationalId: null,
            NullIfEmpty(Get(FanavaranImportFields.Plate)),
            NullIfEmpty(Get(FanavaranImportFields.Vin)),
            NullIfEmpty(Get(FanavaranImportFields.Chassis)),
            VehicleMake: null,
            VehicleModel: null,
            VehicleYear: null,
            issueDate, startDate, endDate,
            // §4.1: "Total premium with tax — RIALS → ÷10" — a fact of this specific export, not
            // an operator choice like the generic path's amountsAreInRials flag.
            totalPremiumRials / 10m,
            contractName), null);
    }
}
