using Aqsat.Updater;
using Aqsat.Updater.Auth;
using Aqsat.Updater.Backup;
using Aqsat.Updater.Docker;
using Aqsat.Updater.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IPackageSignatureVerifier, PackageSignatureVerifier>();
builder.Services.AddSingleton<IPreUpdateBackupService, PreUpdateBackupService>();
builder.Services.AddHttpClient<IContainerOrchestrator, DockerContainerOrchestrator>();
builder.Services.AddSingleton<UpdateOrchestrator>();

var app = builder.Build();

// docs/UPDATE-SYSTEM.md §1 — every route requires the shared token; there is no anonymous route,
// not even /status. This service has nothing to say to anyone but Aqsat.Api.
app.UseMiddleware<SharedTokenAuthMiddleware>();

app.MapGet("/status", () => Results.Ok(new { version = ThisAssemblyVersion.Version, utcNow = DateTimeOffset.UtcNow }));

app.MapPost("/update", (UpdateManifest manifest, UpdateOrchestrator orchestrator, CancellationToken ct) =>
{
    var (accepted, runId, rejectionReason) = orchestrator.Start(manifest, ct);
    return accepted
        ? Results.Accepted($"/progress/{runId}", new { runId })
        : Results.UnprocessableEntity(new { error = rejectionReason });
});

app.MapGet("/progress/{runId:guid}", (Guid runId, UpdateOrchestrator orchestrator) =>
{
    var progress = orchestrator.GetProgress(runId);
    return progress is null ? Results.NotFound() : Results.Ok(progress);
});

app.MapPost("/rollback", (RollbackRequest request, UpdateOrchestrator orchestrator, CancellationToken ct) =>
{
    var (accepted, runId) = orchestrator.StartRollback(request.ToImageTag, ct);
    return accepted ? Results.Accepted($"/progress/{runId}", new { runId }) : Results.UnprocessableEntity();
});

app.Run();

// No `public partial class Program` marker here (unlike Aqsat.Api's Program.cs) — this project
// isn't exercised via WebApplicationFactory<Program> in any test yet, and adding one would collide
// with Aqsat.Api's own Program type the moment both projects are referenced by the same test
// assembly (CS0433). Add it back only alongside an extern-alias fix if that's ever needed.

file static class ThisAssemblyVersion
{
    public static readonly string Version =
        typeof(ThisAssemblyVersion).Assembly.GetName().Version?.ToString() ?? "unknown";
}
