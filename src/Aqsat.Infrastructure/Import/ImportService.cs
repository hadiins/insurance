using System.Security.Cryptography;
using System.Text.Json;
using Aqsat.Application.Common;
using Aqsat.Application.Import;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Import;

/// <summary>
/// The generic import pipeline (docs/PHASE-1-SPEC.md §4.3, docs/TASKS.md Task 6): upload → preview
/// → column mapping → validate → commit → batch report. Task 7's Fanavaran parser is a concrete
/// adapter over this same CommitAsync — it just supplies a hardcoded mapping and pre-processes a
/// few fields (name/code splitting, contract-name stripping) before calling in.
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
        }
        else
        {
            existing.MappingJson = json;
        }

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
        var fileHash = Convert.ToHexString(SHA256.HashData(fileBytes));

        using var stream = new MemoryStream(fileBytes);
        var sheet = workbookReader.ReadFirstSheet(stream);

        var columnIndex = new Dictionary<string, int>();
        foreach (var (targetKey, sourceHeader) in mapping)
        {
            var index = sheet.Headers
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

        var batch = new ImportBatch { AgencyId = agencyId, FileName = fileName, FileHash = fileHash };
        dbContext.ImportBatches.Add(batch);
        await dbContext.SaveChangesAsync(ct);

        var newCount = 0;
        var duplicateCount = 0;
        var failedCount = 0;
        var rowRecords = new List<ImportRow>();

        for (var rowIndex = 0; rowIndex < sheet.Rows.Count; rowIndex++)
        {
            var row = sheet.Rows[rowIndex];
            var rowNumber = rowIndex + 1;

            string Get(string targetKey) =>
                columnIndex.TryGetValue(targetKey, out var idx) && idx < row.Count ? row[idx].Trim() : string.Empty;

            var (validated, validationError) = Validate(Get, dateFormat);
            if (validated is null)
            {
                rowRecords.Add(NewRow(agencyId, batch.Id, rowNumber, ImportRowStatus.Failed, validationError));
                failedCount++;
                continue;
            }

            // docs/PHASE-1-SPEC.md §2 states the dedupe key as "PolicyNumber + SeqNo", but Policy
            // has no SeqNo — that field belongs to Installment (§3.5's cartable/reminder context).
            // Task 6 imports policies, not installments (schedule generation is Task 8), so
            // PolicyNumber alone is the dedupe key here, matching the Check's own wording exactly
            // ("import the same file twice — second run reports 100% duplicates").
            var alreadyExists = await dbContext.Policies.AsNoTracking()
                .AnyAsync(p => p.PolicyNumber == validated.PolicyNumber, ct);
            if (alreadyExists)
            {
                rowRecords.Add(NewRow(agencyId, batch.Id, rowNumber, ImportRowStatus.Duplicate));
                duplicateCount++;
                continue;
            }

            try
            {
                await CreatePolicyAsync(agencyId, batch.Id, validated, Get, amountsAreInRials, ct);
                rowRecords.Add(NewRow(agencyId, batch.Id, rowNumber, ImportRowStatus.New));
                newCount++;
            }
            catch (DbUpdateException)
            {
                // A row failing to persist must never abort the batch (CLAUDE.md rule: a failed
                // row never aborts the batch). Detach whatever this attempt added so the tracker
                // doesn't retry stale/invalid entities on the next row's SaveChanges.
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
        Guid agencyId,
        Guid batchId,
        ValidatedRow validated,
        Func<string, string> get,
        bool amountsAreInRials,
        CancellationToken ct)
    {
        var customer = await dbContext.Customers.FirstOrDefaultAsync(c => c.ExternalCode == validated.ExternalCode, ct);
        if (customer is null)
        {
            customer = new Customer
            {
                AgencyId = agencyId,
                ExternalCode = validated.ExternalCode,
                FullName = validated.FullName,
                Mobile = NullIfEmpty(get(ImportTargetFields.CustomerMobile)),
            };

            var nationalId = get(ImportTargetFields.CustomerNationalId);
            if (!string.IsNullOrEmpty(nationalId))
            {
                customer.NationalId = nationalId;
                customer.NationalIdHash = fieldEncryptor.Hash(nationalId);
            }

            dbContext.Customers.Add(customer);
        }

        var vehicle = new Vehicle
        {
            AgencyId = agencyId,
            Plate = NullIfEmpty(get(ImportTargetFields.VehiclePlate)),
            Vin = NullIfEmpty(get(ImportTargetFields.VehicleVin)),
            Chassis = NullIfEmpty(get(ImportTargetFields.VehicleChassis)),
            Make = NullIfEmpty(get(ImportTargetFields.VehicleMake)),
            Model = NullIfEmpty(get(ImportTargetFields.VehicleModel)),
        };
        if (int.TryParse(get(ImportTargetFields.VehicleYear), out var year))
        {
            vehicle.Year = year;
        }

        dbContext.Vehicles.Add(vehicle);

        var totalPremium = amountsAreInRials ? validated.TotalPremium / 10m : validated.TotalPremium;

        var policy = new Policy
        {
            AgencyId = agencyId,
            PolicyNumber = validated.PolicyNumber,
            Customer = customer,
            Vehicle = vehicle,
            ContractName = validated.ContractName,
            // IsInstallment stays false here — resolved by Task 8's contract→template matching,
            // never guessed during import.
            IsInstallment = false,
            IssueDate = validated.IssueDate,
            StartDate = validated.StartDate,
            EndDate = validated.EndDate,
            TotalPremium = totalPremium,
            DownPayment = 0,
            InstallmentCount = 0,
            ImportBatchId = batchId,
        };
        dbContext.Policies.Add(policy);

        await dbContext.SaveChangesAsync(ct);
    }

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

    private sealed record ValidatedRow(
        string PolicyNumber,
        string ExternalCode,
        string FullName,
        DateOnly IssueDate,
        DateOnly StartDate,
        DateOnly EndDate,
        decimal TotalPremium,
        string ContractName);

    private static (ValidatedRow? Row, string? Error) Validate(Func<string, string> get, DetectedDateFormat dateFormat)
    {
        var policyNumber = get(ImportTargetFields.PolicyNumber);
        if (string.IsNullOrWhiteSpace(policyNumber))
        {
            return (null, "شمارهٔ بیمه‌نامه خالی است.");
        }

        var externalCode = get(ImportTargetFields.CustomerExternalCode);
        if (string.IsNullOrWhiteSpace(externalCode))
        {
            return (null, "کد بیمه‌گذار خالی است.");
        }

        var fullName = get(ImportTargetFields.CustomerFullName);
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (null, "نام بیمه‌گذار خالی است.");
        }

        if (!ImportDateParser.TryParse(get(ImportTargetFields.IssueDate), dateFormat, out var issueDate))
        {
            return (null, "تاریخ صدور نامعتبر است.");
        }

        if (!ImportDateParser.TryParse(get(ImportTargetFields.StartDate), dateFormat, out var startDate))
        {
            return (null, "تاریخ شروع نامعتبر است.");
        }

        if (!ImportDateParser.TryParse(get(ImportTargetFields.EndDate), dateFormat, out var endDate))
        {
            return (null, "تاریخ پایان نامعتبر است.");
        }

        var premiumRaw = get(ImportTargetFields.TotalPremium).Replace(",", "");
        if (!decimal.TryParse(premiumRaw, out var totalPremium) || totalPremium <= 0)
        {
            return (null, "حق بیمه نامعتبر است.");
        }

        var contractName = get(ImportTargetFields.ContractName);
        if (string.IsNullOrWhiteSpace(contractName))
        {
            return (null, "نام قرارداد خالی است.");
        }

        return (new ValidatedRow(policyNumber, externalCode, fullName, issueDate, startDate, endDate, totalPremium, contractName), null);
    }
}
