using System.Text.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Application.Import;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Import;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

[ApiController]
[Route("api/imports")]
[Authorize(Policy = Permissions.ImportRun)]
public sealed class ImportsController(ImportService importService, ICurrentUserContext currentUser, AppDbContext dbContext) : ControllerBase
{
    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<ImportBatchDto>>> History(CancellationToken ct)
    {
        var batches = await dbContext.ImportBatches.AsNoTracking()
            .OrderByDescending(b => b.BizId)
            .Take(100)
            .Select(b => new ImportBatchDto(b.Id, b.FileName, b.NewCount, b.DuplicateCount, b.FailedCount))
            .ToListAsync(ct);

        return Ok(batches);
    }

    [HttpGet("mismatches")]
    public async Task<ActionResult<IReadOnlyList<ImportMismatchRowDto>>> Mismatches([FromQuery] Guid? batchId, CancellationToken ct)
    {
        var query = dbContext.ImportRows.AsNoTracking()
            .Where(r => r.Status == ImportRowStatus.Failed)
            .Include(r => r.ImportBatch)
            .AsQueryable();

        if (batchId is { } id)
        {
            query = query.Where(r => r.ImportBatchId == id);
        }

        var rows = await query
            .OrderByDescending(r => r.BizId)
            .Take(300)
            .Select(r => new ImportMismatchRowDto(r.Id, r.ImportBatchId, r.ImportBatch.FileName, r.RowNumber, r.ErrorMessage))
            .ToListAsync(ct);

        return Ok(rows);
    }


    // Generous for a few-hundred-row xlsx; guards against an accidental huge upload.
    private const long MaxFileSizeBytes = 20 * 1024 * 1024;

    [HttpPost("preview")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<ActionResult<ImportPreviewResponse>> Preview(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            return ValidationProblem("فایل خالی است.");
        }

        var bytes = await ReadAllBytesAsync(file, ct);
        if (!LooksLikeXlsx(bytes))
        {
            return ValidationProblem("فایل ارسالی یک فایل اکسل معتبر نیست.");
        }

        ImportPreview preview;
        try
        {
            preview = importService.BuildPreview(bytes);
        }
        catch (InvalidOperationException ex)
        {
            // Includes ClosedXmlWorkbookReader's row cap — a too-large sheet is a caller error
            // (400 with the Persian reason), never a 500.
            return ValidationProblem(ex.Message);
        }

        return Ok(new ImportPreviewResponse(
            preview.Columns
                .Select(c => new ImportPreviewColumnDto(c.Header, c.DateFormat.ToString(), c.CurrencyScale.ToString()))
                .ToList(),
            preview.SampleRows,
            preview.TargetFields
                .Select(f => new ImportTargetFieldDto(f.Key, f.Label, f.Required, f.Type.ToString()))
                .ToList()));
    }

    [HttpGet("fanavaran/fields")]
    public ActionResult<IReadOnlyList<ImportTargetFieldDto>> GetFanavaranFields() =>
        Ok(FanavaranImportFields.All.Select(f => new ImportTargetFieldDto(f.Key, f.Label, f.Required, f.Type.ToString())).ToList());

    [HttpGet("column-mapping")]
    public async Task<ActionResult<ColumnMappingResponse>> GetColumnMapping([FromQuery] string importType, CancellationToken ct)
    {
        var mapping = await importService.GetSavedMappingAsync(importType, ct);
        return Ok(new ColumnMappingResponse(mapping));
    }

    [HttpPut("column-mapping")]
    public async Task<IActionResult> SaveColumnMapping(ColumnMappingRequest request, CancellationToken ct)
    {
        await importService.SaveMappingAsync(request.ImportType, currentUser.ActiveOrganizationId, request.Mapping, ct);
        return NoContent();
    }

    [HttpPost("commit")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<ActionResult<ImportCommitResponse>> Commit(
        [FromForm] IFormFile file, [FromForm] string meta, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            return ValidationProblem("فایل خالی است.");
        }

        ImportCommitMeta? parsedMeta;
        try
        {
            parsedMeta = JsonSerializer.Deserialize<ImportCommitMeta>(meta, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return ValidationProblem("اطلاعات نگاشت نامعتبر است.");
        }

        if (parsedMeta is null)
        {
            return ValidationProblem("اطلاعات نگاشت نامعتبر است.");
        }

        if (!Enum.TryParse<DetectedDateFormat>(parsedMeta.DateFormat, out var dateFormat)
            || dateFormat == DetectedDateFormat.Unknown)
        {
            return ValidationProblem("فرمت تاریخ باید به‌صراحت مشخص شود.");
        }

        var bytes = await ReadAllBytesAsync(file, ct);
        if (!LooksLikeXlsx(bytes))
        {
            return ValidationProblem("فایل ارسالی یک فایل اکسل معتبر نیست.");
        }

        try
        {
            var report = await importService.CommitAsync(
                bytes,
                file.FileName,
                currentUser.ActiveOrganizationId,
                parsedMeta.Mapping,
                dateFormat,
                parsedMeta.AmountsAreInRials,
                ct);
            return Ok(new ImportCommitResponse(report.BatchId, report.NewCount, report.DuplicateCount, report.FailedCount));
        }
        catch (InvalidOperationException ex)
        {
            // Same contract as the Fanavaran endpoint: a structurally invalid file (including the
            // reader's row cap) is a validation problem with its Persian reason, not a 500.
            return ValidationProblem(ex.Message);
        }
    }

    /// <summary>
    /// Task 7's adapter over the same pipeline: fixed sheet ("CarSalesBNVer"), the agency's saved
    /// FanavaranPolicyReport column mapping, and Fanavaran-specific field parsing — no dateFormat/
    /// amountsAreInRials from the caller, both are fixed facts of this export format (§4.1).
    /// </summary>
    [HttpPost("fanavaran/commit")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<ActionResult<ImportCommitResponse>> CommitFanavaran(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            return ValidationProblem("فایل خالی است.");
        }

        var bytes = await ReadAllBytesAsync(file, ct);
        if (!LooksLikeXlsx(bytes))
        {
            return ValidationProblem("فایل ارسالی یک فایل اکسل معتبر نیست.");
        }

        try
        {
            var report = await importService.CommitFanavaranPolicyReportAsync(
                bytes, file.FileName, currentUser.ActiveOrganizationId, ct);
            return Ok(new ImportCommitResponse(report.BatchId, report.NewCount, report.DuplicateCount, report.FailedCount));
        }
        catch (InvalidOperationException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    /// <summary>An xlsx is a ZIP package — every valid one starts with the "PK" zip signature.
    /// Content-Type headers are browser-controlled and untrustworthy; this cheap byte check keeps
    /// random payloads (HTML, JSON, executables) out of ClosedXML before it parses anything.</summary>
    private static bool LooksLikeXlsx(byte[] bytes) =>
        bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B;

    private static async Task<byte[]> ReadAllBytesAsync(IFormFile file, CancellationToken ct)
    {
        using var memory = new MemoryStream();
        await file.CopyToAsync(memory, ct);
        return memory.ToArray();
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
