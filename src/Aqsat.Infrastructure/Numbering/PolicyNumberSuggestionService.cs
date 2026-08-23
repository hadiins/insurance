using System.Globalization;
using Aqsat.Application.Numbering;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Numbering;

/// <summary>docs/TASK-24-POLICY-NUMBER.md §2/§3 — everything the issuance form needs to render the
/// locked line/agency/year segments plus a suggested serial, without the form ever generating the
/// official number itself (that stays the insurer's).</summary>
public sealed record PolicyNumberSuggestion(
    string InsurerName,
    string Separator,
    string? LineCode,
    string? AgencyCode,
    int Year,
    string YearDisplay,
    int SerialLength,
    string SuggestedSerial,
    string? LastSerial,
    DateOnly? LastIssueDate,
    string? ComposedPreview,
    bool CanCompose);

public sealed class PolicyNumberSuggestionService(AppDbContext dbContext)
{
    private static readonly PersianCalendar Persian = new();

    public async Task<PolicyNumberSuggestion> GetSuggestionAsync(
        Guid agencyId, Guid insuranceLineId, DateOnly issueDate, CancellationToken ct)
    {
        await PolicyNumberDefaultsSeeder.EnsureAgencyDefaultsAsync(dbContext, agencyId, ct);

        var format = await dbContext.PolicyNumberFormats.AsNoTracking()
            .Where(f => f.IsActive)
            .FirstOrDefaultAsync(ct);

        var organization = await dbContext.Organizations.AsNoTracking().FirstAsync(o => o.Id == agencyId, ct);

        var year = Persian.GetYear(issueDate.ToDateTime(TimeOnly.MinValue));
        var yearDisplay = format is { YearDigits: 3 } ? (year % 1000).ToString("D3") : year.ToString("D4");

        var lineCode = format is null
            ? null
            : await dbContext.InsuranceLineCodes.AsNoTracking()
                .Where(c => c.InsuranceLineId == insuranceLineId && c.IsActive && c.InsurerName == format.InsurerName)
                .Select(c => c.Code)
                .FirstOrDefaultAsync(ct);

        var serialLength = format?.SerialLength ?? 6;

        // docs/TASK-24-POLICY-NUMBER.md §3 — serial is shared across every line within the same
        // agency+year, never scoped to a single line. PnSerial stays a string (leading zeros), so
        // the numeric max is taken in memory rather than via a SQL CAST EF cannot translate anyway.
        var candidates = await dbContext.Policies.AsNoTracking()
            .Where(p => p.PnIsParsed && p.PnYear == year && p.PnSerial != null)
            .Select(p => new { p.PnSerial, p.IssueDate })
            .ToListAsync(ct);

        string? lastSerial = null;
        DateOnly? lastIssueDate = null;
        var best = candidates
            .Select(c => (c.PnSerial, c.IssueDate, Numeric: int.TryParse(c.PnSerial, out var n) ? n : -1))
            .Where(c => c.Numeric >= 0)
            .OrderByDescending(c => c.Numeric)
            .FirstOrDefault();
        if (best.PnSerial is not null)
        {
            lastSerial = best.PnSerial;
            lastIssueDate = best.IssueDate;
        }

        var nextSerialNumber = (lastSerial is not null && int.TryParse(lastSerial, out var lastNumeric) ? lastNumeric : 0) + 1;
        var suggestedSerial = nextSerialNumber.ToString().PadLeft(serialLength, '0');

        var canCompose = format is not null && lineCode is not null && organization.AgencyCode is not null;
        var composedPreview = canCompose
            ? PolicyNumberParser.Compose(lineCode!, organization.AgencyCode!, year, suggestedSerial, format!)
            : null;

        return new PolicyNumberSuggestion(
            format?.InsurerName ?? PolicyNumberDefaultsSeeder.ParsianInsurer,
            format?.Separator ?? "/",
            lineCode,
            organization.AgencyCode,
            year,
            yearDisplay,
            serialLength,
            suggestedSerial,
            lastSerial,
            lastIssueDate,
            composedPreview,
            canCompose);
    }
}
