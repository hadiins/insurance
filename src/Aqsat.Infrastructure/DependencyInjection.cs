using Aqsat.Application.ApiIr;
using Aqsat.Application.Common;
using Aqsat.Application.Concurrency;
using Aqsat.Application.Platform;
using Aqsat.Application.Schedule;
using Aqsat.Application.Sms;
using Aqsat.Infrastructure.ApiIr;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Concurrency;
using Aqsat.Infrastructure.Import;
using Aqsat.Infrastructure.Jobs;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Platform;
using Aqsat.Infrastructure.Schedule;
using Aqsat.Infrastructure.Security;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aqsat.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, bool enableHangfireServer = true)
    {
        services.AddSingleton<AgencySessionContextInterceptor>();
        services.AddScoped<ICurrentAgencyAccessor, AgencyContextAccessor>();
        services.AddScoped<IScopeGuard, ScopeGuard>();
        services.AddSingleton<IFieldEncryptor, AesFieldEncryptor>();

        services.AddScoped<ICurrentUserContext, CurrentUserContextAccessor>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<JwtTokenService>();

        services.AddSingleton<IWorkbookReader, ClosedXmlWorkbookReader>();
        services.AddScoped<ImportService>();

        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseSqlServer(configuration.GetConnectionString("Default"))
            .AddInterceptors(sp.GetRequiredService<AgencySessionContextInterceptor>()));

        // docs/TASKS.md Task 14 — real api.ir-backed IHolidayChecker, replacing the Friday-only stub
        // (which ApiIrHolidayChecker still falls back to). Scoped, not singleton, because it now
        // depends on AppDbContext (for cost logging) via IApiIrClient.
        services.AddMemoryCache();
        services.Configure<ApiIrOptions>(configuration.GetSection(ApiIrOptions.SectionName));
        services.AddHttpClient<IApiIrClient, ApiIrClient>((sp, http) =>
            {
                var baseUrl = sp.GetRequiredService<IOptions<ApiIrOptions>>().Value.BaseUrl;
                http.BaseAddress = new Uri(baseUrl);
            })
            .AddStandardResilienceHandler();
        services.AddScoped<ISmsSender, ApiIrSmsSender>();
        // Scoped, not Singleton — it depends on the Scoped ISmsSender, and a Singleton capturing a
        // Scoped dependency is exactly the captive-dependency bug ASP.NET Core's DI validation
        // (ValidateScopes, on by default under the Development environment every test runs under)
        // catches at builder.Build() time. The OTP codes themselves still persist correctly across
        // requests regardless — they live in IMemoryCache, which really is a singleton.
        services.AddScoped<IPlatformOtpService, PlatformOtpService>();
        services.AddSingleton<IMaintenanceModeService, MaintenanceModeService>();
        // docs/TASKS.md Task 23 — only actually resolved when POST /api/platform/updates/register
        // is hit, so an unset Updater:SigningPublicKeyPem (the normal state until Aqsat.Updater is
        // deployed) never breaks startup, only that one endpoint.
        services.AddSingleton<IPackageSignatureVerifier, PackageSignatureVerifier>();

        // docs/TASKS.md Task 22 — the only outbound call from Aqsat.Api to Aqsat.Updater. Base
        // address defaults to the compose service name; unset/misconfigured just means the panel's
        // calls fail closed (no update can start), never silently no-ops.
        services.AddHttpClient<IUpdaterClient, UpdaterClient>((sp, http) =>
        {
            var baseUrl = sp.GetRequiredService<IConfiguration>()["Updater:BaseUrl"] ?? "http://aqsat-updater:8081";
            http.BaseAddress = new Uri(baseUrl);
        });
        services.AddScoped<IHolidayChecker, ApiIrHolidayChecker>();

        services.AddScoped<ILockService, RecordLockService>();
        services.AddSingleton<PresenceConnectionRegistry>();
        services.AddScoped<IPresenceService, PresenceService>();

        services.AddScoped<DeadlineRecalculationJob>();
        services.AddScoped<PresenceAndLockSweepJob>();
        services.AddScoped<SmsReminderJob>();
        services.AddScoped<RenewalWatchJob>();
        services.AddScoped<DatabaseBackupJob>();
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(configuration.GetConnectionString("Default")));

        // The worker that actually executes jobs. Skipped under the test host — recurring jobs
        // iterate every agency on a real timer and would write to the same shared LocalDB that test
        // fixtures use, corrupting unrelated tests' freshly-seeded data.
        if (enableHangfireServer)
        {
            services.AddHangfireServer();
        }

        return services;
    }
}
