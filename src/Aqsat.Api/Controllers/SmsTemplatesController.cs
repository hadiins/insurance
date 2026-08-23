using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Infrastructure.Jobs;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// قالب پیامک‌ها — every known reminder template (Aqsat.Infrastructure.Jobs), each independently
/// customizable per agency. Absence of a saved SmsTemplate row means the hard-coded default is
/// still in effect, never a broken send.
/// </summary>
[ApiController]
[Route("api/sms/templates")]
[Authorize(Policy = Permissions.SettingsWrite)]
public sealed class SmsTemplatesController(AppDbContext dbContext, ICurrentUserContext currentUser) : ControllerBase
{
    private static readonly (string Key, string Label, string Placeholders, string Default)[] Catalog =
    [
        ("installment-reminder-v1", "یادآوری قسط (مشتری)", "{PolicyNumber} {SeqNo} {Balance} {DueDate}", InstallmentReminderTemplate.DefaultBody),
        ("renewal-reminder-customer-v1", "یادآوری تمدید (مشتری)", "{LineName} {ExpiryDate}", RenewalReminderTemplate.CustomerDefaultBody),
        ("renewal-reminder-marketer-v1", "یادآوری تمدید (بازاریاب)", "{LineName} {ExpiryDate}", RenewalReminderTemplate.MarketerDefaultBody),
    ];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SmsTemplateDto>>> List(CancellationToken ct)
    {
        var overrides = await dbContext.SmsTemplates.AsNoTracking()
            .ToDictionaryAsync(t => t.Key, t => t.Body, ct);

        var result = Catalog
            .Select(c => new SmsTemplateDto(
                c.Key, c.Label, c.Placeholders, overrides.GetValueOrDefault(c.Key, c.Default), overrides.ContainsKey(c.Key)))
            .ToList();

        return Ok(result);
    }

    [HttpPut("{key}")]
    public async Task<ActionResult<SmsTemplateDto>> Save(string key, SaveSmsTemplateRequest request, CancellationToken ct)
    {
        var catalogEntry = Catalog.FirstOrDefault(c => c.Key == key);
        if (catalogEntry.Key is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return ValidationProblem("متن قالب نمی‌تواند خالی باشد.");
        }

        var existing = await dbContext.SmsTemplates.FirstOrDefaultAsync(t => t.Key == key, ct);
        if (existing is null)
        {
            existing = new SmsTemplate { AgencyId = currentUser.ActiveOrganizationId, Key = key, Body = request.Body.Trim() };
            dbContext.SmsTemplates.Add(existing);
        }
        else
        {
            existing.Body = request.Body.Trim();
        }

        await dbContext.SaveChangesAsync(ct);

        return Ok(new SmsTemplateDto(catalogEntry.Key, catalogEntry.Label, catalogEntry.Placeholders, existing.Body, true));
    }

    /// <summary>Removes the override — the template goes back to the hard-coded default.</summary>
    [HttpDelete("{key}")]
    public async Task<ActionResult> Reset(string key, CancellationToken ct)
    {
        var existing = await dbContext.SmsTemplates.FirstOrDefaultAsync(t => t.Key == key, ct);
        if (existing is null)
        {
            return NoContent();
        }

        existing.IsDeleted = true;
        existing.DeletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
