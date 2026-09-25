using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// The collection log behind «آخرین تماس» / «بدون تماس» and «قول پرداخت» on the Today dashboard: who
/// was called, how it went, and what they promised.
///
/// A promise is not a status column. "Broken" is derived from PromisedOn having passed with a
/// balance still open, exactly as the settlement countdown derives its own state from dates — so a
/// promise can never be left stale in the database by a job that did not run.
///
/// The audit row is automatic (CollectionContact implements IAuditableEntity), which also satisfies
/// rule 28: it carries the PolicyId even when the contact names a specific installment.
/// </summary>
[ApiController]
[Route("api/collection-contacts")]
[Authorize(Policy = Permissions.PolicyRead)]
public sealed class CollectionContactsController(
    AppDbContext dbContext, ICurrentUserContext currentUser, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CollectionContactDto>>> List(
        [FromQuery] Guid? policyId, [FromQuery] Guid? installmentId, CancellationToken ct)
    {
        if (policyId is null && installmentId is null)
        {
            // Rule 17: an unscoped query would silently return the whole agency's contact log,
            // which looks identical to "this policy has a lot of contacts".
            return ValidationProblem("برای دیدن تاریخچهٔ تماس‌ها باید شناسهٔ بیمه‌نامه یا قسط را بفرستید.");
        }

        // The subject is checked for visibility before the log is read. A subject in another agency
        // is invisible to RLS, and asking for its contacts would return an empty list — the exact
        // "no data" / "no access" ambiguity rule 17 forbids.
        if (policyId is { } requestedPolicy
            && !await dbContext.Policies.AsNoTracking().AnyAsync(p => p.Id == requestedPolicy, ct))
        {
            return NotFound();
        }

        if (installmentId is { } requestedInstallment
            && !await dbContext.Installments.AsNoTracking().AnyAsync(i => i.Id == requestedInstallment, ct))
        {
            return NotFound();
        }

        var query = dbContext.CollectionContacts.AsNoTracking().AsQueryable();
        if (policyId is { } policy)
        {
            query = query.Where(c => c.PolicyId == policy);
        }

        if (installmentId is { } installment)
        {
            query = query.Where(c => c.InstallmentId == installment);
        }

        var rows = await query
            .OrderByDescending(c => c.OccurredAt)
            .Take(200)
            .ToListAsync(ct);

        // The recorder's name comes from a second lookup rather than a join: AppUser is an identity
        // table outside RLS, and a contact list is small enough that one dictionary beats a
        // correlated subquery per row.
        var userIds = rows.Select(r => r.RecordedByUserId).Distinct().ToList();
        var names = await dbContext.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return Ok(rows.Select(r => ToDto(r, names)).ToList());
    }

    [HttpPost]
    [Authorize(Policy = Permissions.PolicyWrite)]
    public async Task<ActionResult<CollectionContactDto>> Create(CreateCollectionContactRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<ContactChannel>(request.Channel, ignoreCase: true, out var channel))
        {
            return ValidationProblem("روش تماس نامعتبر است.");
        }

        if (!Enum.TryParse<ContactOutcome>(request.Outcome, ignoreCase: true, out var outcome))
        {
            return ValidationProblem("نتیجهٔ تماس نامعتبر است.");
        }

        var now = timeProvider.GetUtcNow();
        if (request.OccurredAt > now)
        {
            return ValidationProblem("زمان تماس نمی‌تواند در آینده باشد.");
        }

        // Mirrors the CK_CollectionContacts_Promise check constraint so the caller gets a Persian
        // sentence instead of a raw SQL error — and adds the rule the constraint cannot express:
        // a promise is exactly the outcome that carries these two fields.
        var hasPromise = request.PromisedOn is not null || request.PromisedAmount is not null;
        if (outcome == ContactOutcome.Promised)
        {
            if (request.PromisedOn is null || request.PromisedAmount is null)
            {
                return ValidationProblem("برای قول پرداخت، تاریخ و مبلغ قول هر دو لازم است.");
            }

            if (request.PromisedAmount <= 0)
            {
                return ValidationProblem("مبلغ قول پرداخت باید مثبت باشد.");
            }
        }
        else if (hasPromise)
        {
            return ValidationProblem("تاریخ و مبلغ قول فقط وقتی ثبت می‌شود که نتیجهٔ تماس «قول پرداخت» باشد.");
        }

        // Rule 11: RLS makes an out-of-scope policy invisible, but a foreign key to an invisible row
        // succeeds silently — so the scope of every referenced id is checked here, by hand.
        var policy = await dbContext.Policies.AsNoTracking()
            .Where(p => p.Id == request.PolicyId)
            .Select(p => new { p.Id, p.CustomerId })
            .FirstOrDefaultAsync(ct);
        if (policy is null)
        {
            return NotFound();
        }

        if (request.InstallmentId is { } installmentId)
        {
            var belongs = await dbContext.Installments.AsNoTracking()
                .AnyAsync(i => i.Id == installmentId && i.PolicyId == policy.Id, ct);
            if (!belongs)
            {
                return ValidationProblem("این قسط به این بیمه‌نامه تعلق ندارد.");
            }
        }

        var contact = new CollectionContact
        {
            AgencyId = currentUser.ActiveOrganizationId,
            PolicyId = policy.Id,
            CustomerId = policy.CustomerId,
            InstallmentId = request.InstallmentId,
            Channel = channel,
            Outcome = outcome,
            OccurredAt = request.OccurredAt,
            PromisedOn = outcome == ContactOutcome.Promised ? request.PromisedOn : null,
            PromisedAmount = outcome == ContactOutcome.Promised ? request.PromisedAmount : null,
            RecordedByUserId = currentUser.UserId,
        };

        dbContext.CollectionContacts.Add(contact);
        await dbContext.SaveChangesAsync(ct);

        var names = new Dictionary<Guid, string> { [currentUser.UserId] = currentUser.DisplayName };
        return Ok(ToDto(contact, names));
    }

    private static CollectionContactDto ToDto(CollectionContact c, IReadOnlyDictionary<Guid, string> names) =>
        new(c.Id, c.PolicyId, c.InstallmentId, c.Channel.ToString(), c.Outcome.ToString(),
            c.OccurredAt, c.PromisedOn, c.PromisedAmount,
            names.TryGetValue(c.RecordedByUserId, out var name) ? name : "نامشخص");

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
