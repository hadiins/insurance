using System.Text.Json;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Risk;

/// <summary>
/// The manual-review workflow's write side (docs Phase 2A §15): assignment, the reviewer's final
/// decision, the request-more-info state, and marking a warning read. Every mutation writes its
/// audit row in the same SaveChanges (rule 29) and runs in the caller's RLS scope.
/// </summary>
public sealed class RiskReviewService(AppDbContext dbContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task AssignAsync(
        Guid reviewId, Guid? assignedToUserId, Guid currentUserId, string userDisplayName,
        CancellationToken ct = default)
    {
        var review = await LoadOpenAsync(reviewId, ct);

        string? assigneeName = null;
        if (assignedToUserId is not null)
        {
            assigneeName = await dbContext.Users.AsNoTracking()
                .Where(u => u.Id == assignedToUserId && u.IsActive)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct)
                ?? throw new RiskAssessmentException("کاربر انتخاب‌شده یافت نشد.");
        }

        review.AssignedToUserId = assignedToUserId;
        if (assignedToUserId is not null && review.Status == ManualReviewStatus.Pending)
        {
            review.Status = ManualReviewStatus.InReview;
        }

        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = review.AgencyId,
            UserId = currentUserId,
            UserDisplayName = userDisplayName,
            EntityType = nameof(ManualReview),
            EntityId = review.Id,
            PolicyId = Guid.Empty,
            Action = AuditAction.ManualReviewDecided,
            Description = assigneeName is null
                ? "مسئول بررسی پروندهٔ اعتباری حذف شد"
                : $"بررسی پروندهٔ اعتباری به {assigneeName} واگذار شد",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await dbContext.SaveChangesAsync(ct);
    }

    /// <summary>Doc §15/§20 — the reviewer's final call: APPROVE or REJECT closes the case.</summary>
    public async Task DecideAsync(
        Guid reviewId, RiskDecision finalDecision, string? note,
        Guid currentUserId, string userDisplayName, CancellationToken ct = default)
    {
        if (finalDecision is not (RiskDecision.Approve or RiskDecision.Decline))
        {
            throw new RiskAssessmentException("تصمیم نهایی باید تأیید یا رد باشد.");
        }

        var review = await LoadOpenAsync(reviewId, ct);
        var customer = await dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == review.CustomerId, ct)
            ?? throw new RiskAssessmentException("مشتری یافت نشد.");

        review.Status = finalDecision == RiskDecision.Approve
            ? ManualReviewStatus.Approved
            : ManualReviewStatus.Rejected;
        review.FinalDecision = finalDecision;
        review.AssignedToUserId ??= currentUserId;
        review.Note = note;
        review.ResolvedAt = DateTimeOffset.UtcNow;

        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = review.AgencyId,
            UserId = currentUserId,
            UserDisplayName = userDisplayName,
            EntityType = nameof(ManualReview),
            EntityId = review.Id,
            PolicyId = Guid.Empty,
            Action = AuditAction.ManualReviewDecided,
            Description = finalDecision == RiskDecision.Approve
                ? $"پروندهٔ بررسی اعتباری مشتری {customer.FullName} با تأیید کارشناس بسته شد (امتیاز {review.Assessment.Score})"
                : $"پروندهٔ بررسی اعتباری مشتری {customer.FullName} با رد کارشناس بسته شد (امتیاز {review.Assessment.Score})",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task RequestMoreInfoAsync(
        Guid reviewId, string? note, Guid currentUserId, string userDisplayName, CancellationToken ct = default)
    {
        var review = await LoadOpenAsync(reviewId, ct);

        review.Status = ManualReviewStatus.RequestMoreInfo;
        review.Note = note;

        dbContext.AuditEntries.Add(new AuditEntry
        {
            AgencyId = review.AgencyId,
            UserId = currentUserId,
            UserDisplayName = userDisplayName,
            EntityType = nameof(ManualReview),
            EntityId = review.Id,
            PolicyId = Guid.Empty,
            Action = AuditAction.ManualReviewDecided,
            Description = "برای پروندهٔ بررسی اعتباری اطلاعات تکمیلی درخواست شد",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task MarkWarningReadAsync(Guid warningId, CancellationToken ct = default)
    {
        var warning = await dbContext.RiskWarnings
            .FirstOrDefaultAsync(w => w.Id == warningId, ct)
            ?? throw new RiskAssessmentException("هشدار یافت نشد.");

        if (!warning.IsRead)
        {
            warning.IsRead = true;
            warning.ReadAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);
        }
    }

    /// <summary>The review's snapshot fields, for the detail view — the triggered rules travel as
    /// the same JSON the assessment persisted (composed at write time, rule 31).</summary>
    public async Task<(ManualReview Review, List<TriggeredRule> Rules)> GetReviewDetailAsync(
        Guid reviewId, CancellationToken ct = default)
    {
        var review = await dbContext.ManualReviews.AsNoTracking()
            .Include(r => r.Assessment)
            .Include(r => r.Customer)
            .Include(r => r.AssignedToUser)
            .FirstOrDefaultAsync(r => r.Id == reviewId, ct)
            ?? throw new RiskAssessmentException("پروندهٔ بررسی یافت نشد.");

        var rules = review.Assessment.TriggeredRulesJson is null
            ? []
            : JsonSerializer.Deserialize<List<TriggeredRule>>(review.Assessment.TriggeredRulesJson, JsonOptions) ?? [];
        return (review, rules);
    }

    /// <summary>Reviewers the assignment dropdown offers — the agency's own active users.</summary>
    public async Task<List<(Guid Id, string FullName)>> GetReviewersAsync(CancellationToken ct = default)
    {
        var orgId = AgencyContext.Current ?? Guid.Empty;
        var members = await dbContext.UserOrgRoles.AsNoTracking()
            .Where(m => m.OrganizationId == orgId)
            .Select(m => new { m.UserId, m.User.FullName, m.User.IsActive })
            .ToListAsync(ct);

        return members
            .Where(u => u.IsActive)
            .DistinctBy(u => u.UserId)
            .Select(u => (u.UserId, u.FullName))
            .ToList();
    }

    private async Task<ManualReview> LoadOpenAsync(Guid reviewId, CancellationToken ct)
    {
        var review = await dbContext.ManualReviews
            .Include(r => r.Assessment)
            .FirstOrDefaultAsync(r => r.Id == reviewId, ct)
            ?? throw new RiskAssessmentException("پروندهٔ بررسی یافت نشد.");

        if (review.Status is ManualReviewStatus.Approved or ManualReviewStatus.Rejected)
        {
            throw new RiskAssessmentException("این پروندهٔ بررسی قبلاً بسته شده است.");
        }

        return review;
    }
}
