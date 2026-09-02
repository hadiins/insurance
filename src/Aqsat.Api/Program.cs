using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using Aqsat.Api.Hangfire;
using Aqsat.Api.Hubs;
using Aqsat.Api.Middleware;
using Aqsat.Application.Auth;
using Aqsat.Infrastructure;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Jobs;
using Aqsat.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Threading.RateLimiting;

const string ViteDevCorsPolicy = "ViteDev";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/aqsat-api-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateBootstrapLogger();

try
{
    // CLAUDE.md's UI rule ("Latin digits in inputs and API payloads") only holds if the process
    // itself parses/formats that way. On a Persian-locale host — this app's actual deployment
    // target — the OS default culture uses the Persian calendar, so an unqualified DateOnly/decimal
    // ToString or [FromQuery] bind silently produces or expects Jalali digits instead of ISO/Latin
    // ones. Forcing invariant culture process-wide is what makes every query-string date filter
    // (e.g. /api/reports/pnl?from=&to=) behave the same regardless of the server's OS locale.
    CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
    CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

    // dotnet test hosts the app in-process via WebApplicationFactory, which never sets a distinct
    // environment name for it — the entry assembly is the only reliable signal. Hangfire's recurring
    // jobs iterate every agency and write to the DB on a real timer, which would otherwise run
    // against the same shared LocalDB that test fixtures write to.
    var isTestHost = Assembly.GetEntryAssembly()?.GetName().Name == "testhost";

    var builder = WebApplication.CreateBuilder(args);

    // The test suite wipes its tables on every assembly load, so a WebApplicationFactory-hosted
    // instance must never connect to the developer's real database — route it to the dedicated
    // test database (kept in sync with TestDbContextFactory) unless CI points elsewhere.
    if (isTestHost)
    {
        builder.Configuration["ConnectionStrings:Default"] =
            Environment.GetEnvironmentVariable("AQSAT_TEST_CONNECTION")
            ?? "Server=localhost;Database=AqsatTest;User Id=sa;Password=4Q45BPLZyL8yOWdqCglj;TrustServerCertificate=True;MultipleActiveResultSets=true";
    }

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/aqsat-api-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30));

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    builder.Services.AddInfrastructure(builder.Configuration, enableHangfireServer: !isTestHost);

    var jwtSection = builder.Configuration.GetSection("Jwt");

    // Fail fast on a weak signing key rather than silently running with one — an HS256 key under
    // 48 bytes is brute-forceable, and nothing else in the pipeline checks key strength. The dev
    // appsettings key comfortably exceeds this; only a misconfigured production env var trips it.
    if (!isTestHost && Encoding.UTF8.GetByteCount(jwtSection["Key"] ?? string.Empty) < 48)
    {
        throw new InvalidOperationException(
            "Jwt:Key must be at least 48 bytes of entropy (generate one with: openssl rand -base64 48).");
    }

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            // Keep the "sub" claim as-is — ScopeResolutionMiddleware reads it directly;
            // the default inbound claim map would otherwise rewrite it to a legacy URI claim type.
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtSection["Issuer"],
                ValidateAudience = true,
                ValidAudience = jwtSection["Audience"],
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
            };
            options.Events = new JwtBearerEvents
            {
                // Browser WebSocket/SSE transports can't set a custom Authorization header, so the
                // SignalR JS client sends the token as a query string param instead — accept it only
                // on the hub path, never for ordinary API requests.
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                },
            };
        });

    builder.Services.AddSignalR(options => options.EnableDetailedErrors = builder.Environment.IsDevelopment());
    builder.Services.AddSingleton<IUserIdProvider, SubClaimUserIdProvider>();

    builder.Services.AddAuthorization(options =>
    {
        // One policy per Phase-1 permission (docs/PHASE-1-SPEC.md §2) — ScopeResolutionMiddleware
        // attaches a "permission" claim per permission the caller's resolved role grants.
        foreach (var permission in Permissions.All)
        {
            options.AddPolicy(permission, policy => policy.RequireClaim("permission", permission));
        }
    });

    builder.Services.AddHealthChecks();

    // docs/TASKS.md Task 19 — hardening. Global per-IP window protects the whole API from a runaway
    // client or scraper; "login" is far tighter since it's the one unauthenticated, password-
    // checking endpoint and the obvious brute-force target. Effectively unlimited under the test
    // host — WebApplicationFactory tests call /api/auth/login dozens of times from the same
    // in-memory "IP" across one process, which a real per-IP window would throttle into failures.
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = isTestHost ? int.MaxValue : 300,
                    Window = TimeSpan.FromMinutes(1),
                }));

        options.AddPolicy("login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = isTestHost ? int.MaxValue : 10,
                Window = TimeSpan.FromMinutes(1),
            }));

        // The public portal (api/portal/{token}) is anonymous — its only unauthenticated attack
        // surface is token guessing, so per-IP limits keep a guesser to a crawl without touching
        // real customers. Test host is unlimited like "login": tests probe tokens deliberately.
        options.AddPolicy("portal", httpContext => RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = isTestHost ? int.MaxValue : 20,
                Window = TimeSpan.FromMinutes(1),
            }));
    });

    builder.Services.AddCors(options =>
    {
        options.AddPolicy(ViteDevCorsPolicy, policy => policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
    });

    var app = builder.Build();

    // Must run before anything that reads RemoteIpAddress (rate limiting partitions, audit IP
    // attribution). Only proxies in KnownProxies are trusted; the default is loopback only, so a
    // directly-exposed deployment cannot spoof X-Forwarded-For to rotate rate-limit buckets. When
    // a reverse proxy runs elsewhere (a compose sidecar, an off-host LB), add its IP(s) under
    // "Deployment:KnownProxies" — e.g. ["172.18.0.5"].
    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };
    foreach (var knownProxy in app.Configuration.GetSection("Deployment:KnownProxies").GetChildren())
    {
        if (IPAddress.TryParse(knownProxy.Value, out var proxyIp))
        {
            forwardedHeadersOptions.KnownProxies.Add(proxyIp);
        }
    }
    app.UseForwardedHeaders(forwardedHeadersOptions);

    app.UseExceptionHandler();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();

    // docs/TASKS.md Task 20 — the production image builds the Vite frontend into this API's
    // wwwroot (see src/Aqsat.Api/Dockerfile), so one container serves both. In local dev wwwroot
    // doesn't exist (Vite's own dev server on :5173 serves the frontend instead) — both calls are
    // no-ops against a missing folder, never an error.
    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseCors(ViteDevCorsPolicy);

    app.UseRateLimiter();

    app.UseAuthentication();
    app.UseMiddleware<ScopeResolutionMiddleware>();
    app.UseAuthorization();
    // After authorization (needs the resolved "permission" claims) so Platform.Owner's own bypass
    // check has something to read.
    app.UseMiddleware<MaintenanceModeMiddleware>();

    app.MapControllers();
    app.MapHub<PresenceHub>("/hubs/presence");
    app.MapHub<PlatformHub>("/hubs/platform");

    if (!isTestHost)
    {
        // docs/TASKS.md Task 20 — migration-on-startup with a lock. A rolling deploy can start two
        // replicas against the same database briefly overlapping; EF Core's own migration history
        // table prevents double-applying, but the SAFEST way to avoid two containers racing DDL
        // against each other is never letting them attempt it concurrently in the first place.
        // sp_getapplock is SQL Server's own advisory lock — cheap, connection-scoped, released
        // automatically if the process dies mid-migration instead of leaving a stale lock behind.
        // Off by default only if an operator explicitly disables it (e.g. applying migrations out of
        // band before a blue/green cutover).
        // Same "log and keep starting" reasoning as the Hangfire registration below — a DB that
        // isn't reachable yet must not take the whole process down with it.
        if (app.Configuration.GetValue("Deployment:ApplyMigrationsOnStartup", true))
        {
            // One-time deploy utility: drop the whole database so MigrateAsync below recreates it
            // from scratch — the "wipe all previous data" move between hosting generations where
            // the SQL server is only reachable from inside the host network (no out-of-band wipe
            // possible). MUST be removed from configuration after the first successful start:
            // leaving it on wipes again on every app-pool recycle.
            if (app.Configuration.GetValue("Deployment:ResetDatabaseOnStartup", false))
            {
                try
                {
                    using var resetScope = app.Services.CreateScope();
                    var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
                        resetScope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetDbConnection().ConnectionString);
                    var databaseName = cs.InitialCatalog;
                    cs.InitialCatalog = "master";
                    await using var master = new Microsoft.Data.SqlClient.SqlConnection(cs.ConnectionString);
                    await master.OpenAsync();
                    // SINGLE_USER with ROLLBACK IMMEDIATE kills every other connection first —
                    // a plain DROP fails as long as anything (a previous app instance, a stray
                    // Hangfire server) still holds a connection to the database.
                    await using var quarantine = master.CreateCommand();
                    quarantine.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE";
                    await quarantine.ExecuteNonQueryAsync();
                    await using var drop = master.CreateCommand();
                    drop.CommandText = $"DROP DATABASE [{databaseName}]";
                    await drop.ExecuteNonQueryAsync();
                    Log.Warning("Deployment:ResetDatabaseOnStartup was set — database {Database} dropped; migrations will recreate it empty.", databaseName);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Deployment:ResetDatabaseOnStartup failed — continuing against the existing database.");
                }
            }

            try
            {
                using var migrationScope = app.Services.CreateScope();
                var migrationContext = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var connection = migrationContext.Database.GetDbConnection();
                await connection.OpenAsync();
                await using (var acquire = connection.CreateCommand())
                {
                    acquire.CommandText = "EXEC sp_getapplock @Resource = 'AqsatMigration', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 60000;";
                    await acquire.ExecuteNonQueryAsync();
                }

                try
                {
                    await migrationContext.Database.MigrateAsync();

                    // Global reference data (no AgencyId) that every policy-issuance flow depends
                    // on — idempotent get-or-create, so running it on every startup is safe and is
                    // the only thing that actually seeds it outside of dev/test fixtures. Without
                    // this, a freshly deployed production database has zero InsuranceLine rows and
                    // "ثبت بیمه‌نامه" renders with nothing selectable.
                    await Aqsat.Infrastructure.Seed.InsuranceLineSeeder.EnsureSeededAsync(migrationContext);
                    // The seeded "بازاریاب" system role — what an agency's manager assigns when giving
                    // a marketer panel access (MarketersController's panel-access endpoints). Idempotent
                    // get-or-create, so it lands here alongside the other startup seeding.
                    await Aqsat.Infrastructure.Seed.MarketerRoleSeeder.EnsureSeededAsync(migrationContext);
                    // One-time, idempotent: decrypts the legacy NationalIdEncrypted columns into the new
                    // plaintext NationalId columns and drops the legacy columns once empty (owner
                    // decision 2026-08-28 — CLAUDE.md rule 12 rewritten). SQL cannot decrypt AES-GCM,
                    // so the conversion runs here in the application, before the API starts serving.
                    // It shares the exclusive app-lock above, so concurrent instances cannot
                    // double-convert or drop the column out from under each other.
                    try
                    {
                        var encryptor = migrationScope.ServiceProvider
                            .GetRequiredService<Aqsat.Application.Common.IFieldEncryptor>();
                        var backfillLogger = migrationScope.ServiceProvider
                            .GetRequiredService<Microsoft.Extensions.Logging.ILogger<Aqsat.Infrastructure.Jobs.NationalIdPlaintextBackfillJob>>();
                        await new Aqsat.Infrastructure.Jobs.NationalIdPlaintextBackfillJob(migrationContext, encryptor, backfillLogger)
                            .RunAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to decrypt the legacy NationalIdEncrypted columns at startup — the API will continue starting and the next startup retries. Until the conversion completes, customers issued before it may not be findable by national ID.");
                    }
                }
                finally
                {
                    await using var release = connection.CreateCommand();
                    release.CommandText = "EXEC sp_releaseapplock @Resource = 'AqsatMigration', @LockOwner = 'Session';";
                    await release.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply database migrations at startup — the database may be unreachable. The API will continue starting.");
            }
        }

        // Platform.Owner JWT required — Hangfire's default (loopback-IP-only "authorization") is no
        // authorization at all against SSRF from inside the network, and it would silently break
        // the day requests arrive through a same-host proxy. See PlatformOwnerDashboardFilter.
        app.UseHangfireDashboard(options: new DashboardOptions
        {
            Authorization = [new PlatformOwnerDashboardFilter()],
        });

        // docs/TASKS.md Task 19 — hardening. Registering recurring jobs acquires a distributed lock
        // over a real DB connection, synchronously, during startup. If the database happens to be
        // unreachable at that exact moment (a slow-starting DB container, a transient network blip),
        // an unhandled SqlException here previously crashed the ENTIRE process before it ever bound
        // its port — every request would fail, not just the DB-dependent ones, and health checks
        // couldn't even report "unhealthy" because nothing was listening. Recurring-job registration
        // is idempotent (Hangfire just re-upserts the same schedule), so it's safe to log and move on
        // — the API still starts and serves what it can; Hangfire's own server retries independently.
        try
        {
            RecurringJob.AddOrUpdate<DeadlineRecalculationJob>(
                "settlement-deadline-recalculation",
                job => job.RecalculateAsync(CancellationToken.None),
                Cron.Daily);
            RecurringJob.AddOrUpdate<PresenceAndLockSweepJob>(
                "presence-and-lock-sweep",
                job => job.SweepAsync(CancellationToken.None),
                "*/2 * * * *");
            RecurringJob.AddOrUpdate<SmsReminderJob>(
                "sms-reminders",
                job => job.RunAsync(CancellationToken.None),
                Cron.Daily);
            RecurringJob.AddOrUpdate<RenewalWatchJob>(
                "renewal-watches",
                job => job.RunAsync(CancellationToken.None),
                Cron.Daily);
            RecurringJob.AddOrUpdate<DatabaseBackupJob>(
                "database-backup",
                job => job.RunAsync(CancellationToken.None),
                Cron.Daily(3));
            RecurringJob.AddOrUpdate<AgencyStatsRollupJob>(
                "agency-stats-rollup",
                job => job.RunAsync(CancellationToken.None),
                Cron.Daily(4));
            // Backdated IssueDates (Fanavaran imports carry historical dates) are invisible to the
            // nightly trailing window — a monthly full rebuild is what keeps lifetime totals true.
            RecurringJob.AddOrUpdate<AgencyStatsRollupJob>(
                "agency-stats-full-rebuild",
                job => job.RunFullRebuildAsync(CancellationToken.None),
                Cron.Monthly(1, 5));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to register Hangfire recurring jobs at startup — the database may be unreachable. The API will continue starting.");
        }

        // The one-time PolicyNumberBackfillJob/VehiclePlateBackfillJob enqueue that lived here has
        // been removed — confirmed successful in production (docs/TASK-24-POLICY-NUMBER.md §9 step
        // 5, docs/TASK-25-IDENTITY-VEHICLE.md §7 step 6). Both jobs are still idempotent and
        // re-runnable by hand later (e.g. from a future "بازتجزیهٔ شماره‌ها" admin action) if needed.

        // One-time (idempotent, re-runnable) backfill for AgencyCommissionEntry on policies issued
        // before stage 3/7 of the accounting buildout existed. Remove this enqueue the same way the
        // two above were removed, once confirmed successful in production.
        try
        {
            Hangfire.BackgroundJob.Enqueue<Aqsat.Infrastructure.Jobs.AgencyCommissionBackfillJob>(
                job => job.RunAsync(CancellationToken.None));

            // Recomputes every Customer.NationalIdHash under the new keyed (HMAC-SHA256) algorithm —
            // hashes stored before the switch were unkeyed SHA-256 and are the weak link this
            // hardening pass exists to remove. Re-running after success writes nothing; remove this
            // enqueue once a run has completed cleanly in production.
            Hangfire.BackgroundJob.Enqueue<Aqsat.Infrastructure.Jobs.NationalIdHashBackfillJob>(
                job => job.RunAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enqueue backfill jobs at startup — the database may be unreachable.");
        }
    }

    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() }),
            });
        },
    });

    // Serves the SPA's entry point for any non-API GET — a plain static-file server would 404 a
    // hard refresh once Vite's build hashes the asset filenames. No-op if wwwroot is empty (dev,
    // where Vite's own dev server on :5173 handles this instead).
    app.MapFallbackToFile("index.html");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Aqsat.Api terminated unexpectedly during startup");
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Lets WebApplicationFactory&lt;Program&gt; see this entry point from the test project.</summary>
public partial class Program;
