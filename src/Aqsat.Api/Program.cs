using System.Globalization;
using System.Reflection;
using System.Text;
using Aqsat.Api.Hubs;
using Aqsat.Api.Middleware;
using Aqsat.Application.Auth;
using Aqsat.Infrastructure;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using Serilog;

const string ViteDevCorsPolicy = "ViteDev";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/aqsat-api-.log", rollingInterval: RollingInterval.Day)
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

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/aqsat-api-.log", rollingInterval: RollingInterval.Day));

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    builder.Services.AddInfrastructure(builder.Configuration, enableHangfireServer: !isTestHost);

    var jwtSection = builder.Configuration.GetSection("Jwt");
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

    builder.Services.AddCors(options =>
    {
        options.AddPolicy(ViteDevCorsPolicy, policy => policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
    });

    var app = builder.Build();

    app.UseExceptionHandler();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();

    app.UseCors(ViteDevCorsPolicy);

    app.UseAuthentication();
    app.UseMiddleware<ScopeResolutionMiddleware>();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<PresenceHub>("/hubs/presence");

    if (!isTestHost)
    {
        // Dashboard defaults to local-requests-only authorization — no extra filter needed for Phase 1.
        app.UseHangfireDashboard();
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
