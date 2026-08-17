using System.Text.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Application.Import;
using Aqsat.Infrastructure.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aqsat.Api.Controllers;

[ApiController]
[Route("api/imports")]
[Authorize(Policy = Permissions.ImportRun)]
public sealed class ImportsController(ImportService importService, ICurrentUserContext currentUser) : ControllerBase
{
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
        var preview = importService.BuildPreview(bytes);

        return Ok(new ImportPreviewResponse(
            preview.Columns
                .Select(c => new ImportPreviewColumnDto(c.Header, c.DateFormat.ToString(), c.CurrencyScale.ToString()))
                .ToList(),
            preview.SampleRows,
            preview.TargetFields
                .Select(f => new ImportTargetFieldDto(f.Key, f.Label, f.Required, f.Type.ToString()))
                .ToList()));
    }

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
