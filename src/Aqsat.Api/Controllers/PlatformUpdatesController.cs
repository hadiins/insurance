using System.Reflection;
using Aqsat.Api.Contracts;
using Aqsat.Api.Hubs;
using Aqsat.Application.Auth;
using Aqsat.Application.Common;
using Aqsat.Application.Platform;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// docs/TASKS.md Task 22 — the update panel's backend. Everything here is Platform.Owner-only
/// (docs/UPDATE-SYSTEM.md rule 1: "belongs to you, not any agency" — agency users never see this
/// exists, not even read-only). Actually starting an update additionally requires a fresh OTP
/// (rule 2) verified against the caller's own registered mobile — a password alone never suffices.
/// </summary>
[ApiController]
[Route("api/platform/updates")]
[Authorize(Policy = Permissions.PlatformOwner)]
public sealed class PlatformUpdatesController(
    AppDbContext dbContext,
    ICurrentUserContext currentUser,
    IPlatformOtpService otpService,
    IUpdaterClient updaterClient,
    IMaintenanceModeService maintenanceMode,
    IHubContext<PlatformHub> hub,
    IServiceScopeFactory scopeFactory,
    IPackageSignatureVerifier signatureVerifier,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<PlatformStatusDto> Status() =>
        Ok(new PlatformStatusDto(CurrentVersion, maintenanceMode.IsActive, maintenanceMode.EstimatedEndsAt));

    /// <summary>Single source of truth for "what version am I running", used by both the panel's
    /// status display and Start()'s compatibility gate. Config (App:Version) wins when explicitly
    /// set; otherwise the version stamped into the image at build time (src/Aqsat.Api/Dockerfile's
    /// ARG APP_VERSION → InformationalVersion). Why not a runtime env var: Aqsat.Updater recreates
    /// this container copying the OLD container's env (DockerContainerOrchestrator.RecreateContainerAsync),
    /// so an env-based version would stay stale forever after the first panel-driven update —
    /// misreporting the panel and failing every later version gate (IsOlderThan fails closed on
    /// unparseable versions). A truly unstamped dev build reports "unknown", which Start()'s gate
    /// treats as unparseable → version-gated packages are refused (fail closed).</summary>
    private string CurrentVersion
    {
        get
        {
            var configured = configuration["App:Version"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            // Semver build metadata ("+<git-commit>", appended by the SDK when SourceRevisionId is
            // set — local/non-stamped builds carry it) is NOT part of the version proper: with it
            // intact, IsOlderThan's Version.TryParse fails (gate fails closed) and the value
            // overflows UpdateRuns.FromVersion's column width → SqlException 2628 on Start().
            var informational = typeof(PlatformUpdatesController).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var buildMetadata = informational?.IndexOf('+') ?? -1;
            return buildMetadata >= 0 ? informational![..buildMetadata] : informational ?? "unknown";
        }
    }

    [HttpGet("packages")]
    public async Task<ActionResult<IReadOnlyList<UpdatePackageDto>>> Packages(CancellationToken ct)
    {
        var packages = await dbContext.UpdatePackages.AsNoTracking()
            .Where(p => !p.IsYanked)
            .OrderByDescending(p => p.PublishedAt)
            .Select(p => new UpdatePackageDto(p.Id, p.Version, p.ReleaseNotesFa, p.HasDbMigration, p.IsSecurityUpdate, p.PublishedAt))
            .ToListAsync(ct);

        return Ok(packages);
    }

    /// <summary>docs/TASKS.md Task 23 — called by scripts/release/register-package.sh. Not gated
    /// by OTP (rule 2 only requires it for actually starting an update against production) — this
    /// is a catalog write, run by CI/CD under its own Platform.Owner-authenticated credential, not
    /// an interactive action against a live agency.</summary>
    [HttpPost("register")]
    public async Task<ActionResult<UpdatePackageDto>> Register(RegisterPackageRequest request, CancellationToken ct)
    {
        if (!signatureVerifier.Verify(request.Version, request.ImageTag, request.Sha256, request.SignatureBase64))
        {
            return ValidationProblem("امضای بسته نامعتبر است — بستهٔ ثبت نشد.");
        }

        var alreadyExists = await dbContext.UpdatePackages.AsNoTracking().AnyAsync(p => p.Version == request.Version, ct);
        if (alreadyExists)
        {
            return ValidationProblem($"نسخهٔ {request.Version} قبلاً ثبت شده است.");
        }

        var package = new UpdatePackage
        {
            Version = request.Version,
            ImageTag = request.ImageTag,
            Sha256 = request.Sha256,
            SignatureBase64 = request.SignatureBase64,
            ReleaseNotesFa = request.ReleaseNotesFa,
            MinimumFromVersion = request.MinimumFromVersion,
            HasDbMigration = request.HasDbMigration,
            IsSecurityUpdate = request.IsSecurityUpdate,
            PublishedAt = DateTimeOffset.UtcNow,
            IsYanked = false,
        };
        dbContext.UpdatePackages.Add(package);
        await dbContext.SaveChangesAsync(ct);

        return Ok(new UpdatePackageDto(package.Id, package.Version, package.ReleaseNotesFa, package.HasDbMigration, package.IsSecurityUpdate, package.PublishedAt));
    }

    /// <summary>docs/UPDATE-SYSTEM.md §8: "if a version turns out broken, flip IsYanked so it's
    /// never offered to anyone else again." Never deletes the row — the audit trail of "this
    /// version existed and was pulled" is itself useful, and any UpdateRun that already applied it
    /// still needs a real FK target.</summary>
    [HttpPost("packages/{packageId:guid}/yank")]
    public async Task<ActionResult> Yank(Guid packageId, CancellationToken ct)
    {
        var package = await dbContext.UpdatePackages.FirstOrDefaultAsync(p => p.Id == packageId, ct);
        if (package is null)
        {
            return NotFound();
        }

        package.IsYanked = true;
        await dbContext.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpPost("otp")]
    public async Task<ActionResult<RequestOtpResponse>> RequestOtp(CancellationToken ct)
    {
        var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUser.UserId, ct);
        if (user is null || string.IsNullOrWhiteSpace(user.Mobile))
        {
            return ValidationProblem("شمارهٔ همراه کاربر ثبت نشده است.");
        }

        // The send's real outcome is surfaced (api.ir rejection, outage, no credit): the panel must
        // never show "کد ارسال شد" for an SMS that never left the building.
        var sent = await otpService.SendAsync(currentUser.UserId, user.Mobile, currentUser.ActiveOrganizationId, ct);
        return Ok(new RequestOtpResponse(sent, sent ? MaskMobile(user.Mobile) : null));
    }

    [HttpPost("{packageId:guid}/start")]
    public async Task<ActionResult<UpdateRunDto>> Start(Guid packageId, StartUpdateRequest request, CancellationToken ct)
    {
        if (maintenanceMode.IsActive)
        {
            return ValidationProblem("یک به‌روزرسانی دیگر در حال اجراست.");
        }

        if (!otpService.Verify(currentUser.UserId, request.OtpCode))
        {
            return ValidationProblem("کد تأیید نامعتبر یا منقضی‌شده است.");
        }

        var package = await dbContext.UpdatePackages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == packageId && !p.IsYanked, ct);
        if (package is null)
        {
            return ValidationProblem("بستهٔ به‌روزرسانی یافت نشد یا لغو شده است.");
        }

        var currentVersion = CurrentVersion;
        if (!string.IsNullOrWhiteSpace(package.MinimumFromVersion) &&
            IsOlderThan(currentVersion, package.MinimumFromVersion))
        {
            return ValidationProblem($"این نسخه نیازمند حداقل نسخهٔ {package.MinimumFromVersion} است. ابتدا به نسخه‌های میانی به‌روزرسانی کنید.");
        }

        var run = new UpdateRun
        {
            PackageId = package.Id,
            FromVersion = currentVersion,
            ToVersion = package.Version,
            StartedByUserId = currentUser.UserId,
            StartedAt = DateTimeOffset.UtcNow,
            Status = UpdateRunStatus.Running,
            CurrentStage = "PreparingMaintenanceWindow",
            ProgressPercent = 0,
        };
        dbContext.UpdateRuns.Add(run);
        await dbContext.SaveChangesAsync(ct);

        // Fire-and-forget on purpose: an update takes minutes, and docs/UPDATE-SYSTEM.md §5 requires
        // it to survive the caller closing their browser. Its own DI scope (not this request's,
        // which ends the moment this action returns) is what makes that safe.
        _ = Task.Run(() => RunUpdateAsync(run.Id, package, request, ct: CancellationToken.None), CancellationToken.None);

        return Accepted(new UpdateRunDto(run.Id, run.FromVersion, run.ToVersion, currentUser.DisplayName, run.StartedAt, null, run.Status.ToString(), run.CurrentStage, run.ProgressPercent, null, null));
    }

    [HttpPost("rollback")]
    public async Task<ActionResult> Rollback(RollbackRequest request, CancellationToken ct)
    {
        var result = await updaterClient.StartRollbackAsync(request.ToImageTag, ct);
        return result.Accepted ? Accepted(new { runId = result.RunId }) : ValidationProblem(result.RejectionReason ?? "بازگشت ناموفق بود.");
    }

    [HttpGet("current")]
    public async Task<ActionResult<UpdateRunDto?>> Current(CancellationToken ct)
    {
        var run = await dbContext.UpdateRuns.AsNoTracking()
            .Where(r => r.Status == UpdateRunStatus.Running)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(ct);

        return Ok(run is null ? null : await ToDtoAsync(run, ct));
    }

    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<UpdateRunDto>>> History(CancellationToken ct)
    {
        var runs = await dbContext.UpdateRuns.AsNoTracking()
            .Include(r => r.Package)
            .OrderByDescending(r => r.StartedAt)
            .Take(50)
            .ToListAsync(ct);

        var userNames = await dbContext.Users.AsNoTracking()
            .Where(u => runs.Select(r => r.StartedByUserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var dtos = runs.Select(r => new UpdateRunDto(
            r.Id, r.FromVersion, r.ToVersion, userNames.GetValueOrDefault(r.StartedByUserId, "—"), r.StartedAt,
            r.CompletedAt, r.Status.ToString(), r.CurrentStage, r.ProgressPercent, r.ErrorMessage, r.ErrorDetail)).ToList();

        return Ok(dtos);
    }

    [HttpGet("{runId:guid}")]
    public async Task<ActionResult<UpdateRunDetailDto>> Detail(Guid runId, CancellationToken ct)
    {
        var run = await dbContext.UpdateRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
        {
            return NotFound();
        }

        var stages = await dbContext.UpdateStageLogs.AsNoTracking()
            .Where(s => s.RunId == runId)
            .OrderBy(s => s.StageNo)
            .Select(s => new UpdateStageLogDto(s.StageNo, s.StageName, s.StartedAt, s.CompletedAt, s.Succeeded, s.Output))
            .ToListAsync(ct);

        return Ok(new UpdateRunDetailDto(await ToDtoAsync(run, ct), stages));
    }

    /// <summary>docs/UPDATE-SYSTEM.md §5/§7: 60-second warning, THEN maintenance mode, THEN the
    /// actual Updater call, relaying its progress into UpdateRun/UpdateStageLog and SignalR until
    /// it finishes — whatever "finishes" means (success, failure, or rollback).</summary>
    private async Task RunUpdateAsync(Guid runId, UpdatePackage package, StartUpdateRequest request, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scopedUpdaterClient = scope.ServiceProvider.GetRequiredService<IUpdaterClient>();
        var scopedMaintenanceMode = scope.ServiceProvider.GetRequiredService<IMaintenanceModeService>();
        var scopedLogger = scope.ServiceProvider.GetRequiredService<ILogger<PlatformUpdatesController>>();

        // Everything below talks to the network (SignalR, Aqsat.Updater over HTTP) or the DB, with
        // no caller left to observe or retry a failure the way an HTTP action's own exception
        // handler would — an unreachable Updater must not leave the run stuck at "Running" forever
        // with maintenance mode permanently on, it must fail loudly and let the gate back down.
        try
        {
            // Configurable, not a hard-coded 60 (CLAUDE.md's operational-parameters-not-constants
            // rule) — also what lets tests exercise this flow in milliseconds instead of a real minute.
            var warningSeconds = configuration.GetValue("Updater:MaintenanceWarningSeconds", 60);
            await hub.Clients.Group(PlatformHub.AllUsersGroup).SendAsync(
                "MaintenanceWarning", new { secondsRemaining = warningSeconds }, ct);
            await Task.Delay(TimeSpan.FromSeconds(warningSeconds), ct);

            scopedMaintenanceMode.Activate(TimeSpan.FromMinutes(15));
            await hub.Clients.Group(PlatformHub.AllUsersGroup).SendAsync("MaintenanceModeChanged", new { active = true }, ct);

            // Taking the whole platform down for an update is exactly the kind of action the
            // security dashboard's feed exists for — no agency scope is attached to it, so the
            // AuditEntry pipeline never sees it.
            await scope.ServiceProvider.GetRequiredService<SecurityEventWriter>().WriteAsync(
                SecurityEventType.SensitiveSettingChanged, SecuritySeverity.Warning,
                $"حالت تعمیرات برای اجرای به‌روزرسانی نسخهٔ {package.Version} فعال شد",
                cancellationToken: ct);

            var manifest = new UpdaterManifest(package.Version, package.ImageTag, package.Sha256, package.SignatureBase64);
            var start = await scopedUpdaterClient.StartUpdateAsync(manifest, ct);

            if (!start.Accepted || start.RunId is not { } updaterRunId)
            {
                await FailAsync(scopedDb, runId, start.RejectionReason ?? "درخواست به‌روزرسانی رد شد.", ct);
                return;
            }

            var run = await scopedDb.UpdateRuns.FirstAsync(r => r.Id == runId, ct);
            run.UpdaterRunId = updaterRunId;
            await scopedDb.SaveChangesAsync(ct);

            // docs/UPDATE-SYSTEM.md §5: closing the browser must not stop the update. This loop is
            // the reason — it keeps polling and persisting regardless of whether anyone is watching.
            var deadline = DateTimeOffset.UtcNow.AddMinutes(15);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var progress = await scopedUpdaterClient.GetProgressAsync(updaterRunId, ct);
                if (progress is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                    continue;
                }

                run.CurrentStage = progress.Stage;
                run.ProgressPercent = progress.PercentComplete;
                await scopedDb.SaveChangesAsync(ct);
                await hub.Clients.Group(PlatformHub.PlatformOwnersGroup).SendAsync("UpdateProgress", progress, ct);

                if (progress.Status != "Running")
                {
                    run.Status = Enum.Parse<UpdateRunStatus>(progress.Status);
                    run.CompletedAt = DateTimeOffset.UtcNow;
                    run.ErrorMessage = progress.ErrorMessage;
                    await scopedDb.SaveChangesAsync(ct);
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
        catch (Exception ex)
        {
            scopedLogger.LogError(ex, "Update run {RunId} to {ToVersion} failed unexpectedly", runId, package.Version);
            await FailAsync(scopedDb, runId, $"خطای غیرمنتظره: {ex.Message}", ct);
        }
        finally
        {
            scopedMaintenanceMode.Deactivate();
            await hub.Clients.Group(PlatformHub.AllUsersGroup).SendAsync("MaintenanceModeChanged", new { active = false }, CancellationToken.None);
        }
    }

    private static async Task FailAsync(AppDbContext scopedDb, Guid runId, string reason, CancellationToken ct)
    {
        var run = await scopedDb.UpdateRuns.FirstAsync(r => r.Id == runId, ct);
        run.Status = UpdateRunStatus.Failed;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.ErrorMessage = reason;
        await scopedDb.SaveChangesAsync(ct);
    }

    private async Task<UpdateRunDto> ToDtoAsync(UpdateRun run, CancellationToken ct)
    {
        var startedByName = await dbContext.Users.AsNoTracking()
            .Where(u => u.Id == run.StartedByUserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "—";

        return new UpdateRunDto(
            run.Id, run.FromVersion, run.ToVersion, startedByName, run.StartedAt, run.CompletedAt,
            run.Status.ToString(), run.CurrentStage, run.ProgressPercent, run.ErrorMessage, run.ErrorDetail);
    }

    /// <summary>Ordinal string comparison breaks on this exact check ("unknown" sorts after
    /// "99.0.0" lexicographically, silently defeating the whole prerequisite gate) — proper
    /// semantic version parsing instead. An unparseable current version fails closed (treated as
    /// "too old"): refusing an update we can't safely evaluate is the correct default, not
    /// silently allowing a version jump that skips a required migration path.</summary>
    private static bool IsOlderThan(string currentVersion, string minimumVersion) =>
        !Version.TryParse(currentVersion, out var current) || !Version.TryParse(minimumVersion, out var minimum) || current < minimum;

    private static string MaskMobile(string mobile) =>
        mobile.Length < 7 ? mobile : $"{mobile[..4]}•••{mobile[^3..]}";

    private ActionResult ValidationProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });
}
