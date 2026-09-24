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
using Aqsat.Infrastructure.Numbering;
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
        services.AddScoped<PolicyNumberSuggestionService>();

        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseSqlServer(configuration.GetConnectionString("Default"))
            .AddInterceptors(
                sp.GetRequiredService<AgencySessionContextInterceptor>(),
                sp.GetRequiredService<EfCommandDurationInterceptor>()));

        // Platform-owner monitoring pipeline: the aggregator and EF command stats are process-wide
        // singletons (they outlive any request); the security event writer is scoped because it
        // resolves a scoped AppDbContext per write.
        services.AddSingleton<Monitoring.RequestMetricsAggregator>();
        services.AddSingleton<Monitoring.EfCommandDurationStats>();
        services.AddSingleton<EfCommandDurationInterceptor>();
        services.AddScoped<SecurityEventWriter>();

        // Owner decision 2026-09-23 (docs/PHASE-1-SPEC.md §6): the paid api.ir holiday lookup
        // (IsHoliday, 150 Toman per distinct date) is NOT called — the registered IHolidayChecker is
        // the Friday-only WeekendOnlyHolidayChecker below, so a deadline shifts on Fridays only and
        // no IsHoliday call is ever billed. The api.ir path is parked, not deleted:
        // IApiIrClient.IsHolidayAsync is still implemented and tested, so switching the paid lookup
        // back on means restoring the thin ApiIrHolidayChecker adapter and this registration line —
        // no schema change, no data migration.
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
        // Scoped for the same captive-dependency reason as PlatformOtpService above; the codes
        // themselves live in the singleton IMemoryCache.
        services.AddScoped<IAgencyOtpService, AgencyOtpService>();
        // Scoped for the same captive-dependency reason; codes live in the singleton IMemoryCache.
        services.AddScoped<ISignupOtpService, SignupOtpService>();
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
        services.AddScoped<IHolidayChecker, Aqsat.Infrastructure.Schedule.WeekendOnlyHolidayChecker>();

        services.AddScoped<ILockService, RecordLockService>();
        services.AddSingleton<PresenceConnectionRegistry>();
        services.AddScoped<IPresenceService, PresenceService>();

        services.AddScoped<DeadlineRecalculationJob>();
        services.AddScoped<PresenceAndLockSweepJob>();
        services.AddScoped<SmsReminderJob>();
        services.AddScoped<RenewalWatchJob>();
        services.AddScoped<DatabaseBackupJob>();
        services.AddScoped<AgencyStatsRollupJob>();
        services.AddScoped<RiskAssessmentJob>();
        services.AddScoped<Aqsat.Infrastructure.Stats.AgencyStatsService>();
        services.AddScoped<AgencyCommissionBackfillJob>();
        services.AddScoped<NationalIdHashBackfillJob>();
        services.AddScoped<Monitoring.AlertEvaluationService>();
        services.AddScoped<Monitoring.MetricsSamplerJob>();
        services.AddScoped<Aqsat.Infrastructure.Payments.PaymentReversalService>();
        // The portal's gateway abstraction, registered open: the services inject
        // IEnumerable<IPaymentGateway> and pick by Provider, so wiring a PSP is one block here.
        //
        // Mock stays first and needs no credential — a fresh install must still complete the whole
        // portal flow before any PSP account exists. The two real PSPs are typed HttpClients
        // (pooled handlers + standard resilience, same pattern as ApiIrClient above) whose
        // BaseAddress comes from configuration, which is what lets زرينپال be pointed at its
        // sandbox host without a code change. Each is then re-registered behind the interface so
        // all three remain discoverable through the single IEnumerable the services already take.
        services.AddScoped<Aqsat.Infrastructure.Payments.GatewayPaymentCoordinator>();
        services.AddScoped<Aqsat.Application.Payments.IPaymentGateway,
            Aqsat.Infrastructure.Payments.MockPaymentGateway>();

        services.Configure<Aqsat.Infrastructure.Payments.GooyaPayOptions>(
            configuration.GetSection(Aqsat.Infrastructure.Payments.GooyaPayOptions.SectionName));
        services.AddHttpClient<Aqsat.Infrastructure.Payments.GooyaPayPaymentGateway>((sp, http) =>
            {
                var gatewayOptions = sp.GetRequiredService<IOptions<Aqsat.Infrastructure.Payments.GooyaPayOptions>>().Value;
                http.BaseAddress = new Uri(gatewayOptions.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(gatewayOptions.TimeoutSeconds);
            })
            .AddStandardResilienceHandler();
        services.AddScoped<Aqsat.Application.Payments.IPaymentGateway>(
            sp => sp.GetRequiredService<Aqsat.Infrastructure.Payments.GooyaPayPaymentGateway>());

        services.Configure<Aqsat.Infrastructure.Payments.ZarinPalOptions>(
            configuration.GetSection(Aqsat.Infrastructure.Payments.ZarinPalOptions.SectionName));
        services.AddHttpClient<Aqsat.Infrastructure.Payments.ZarinPalPaymentGateway>((sp, http) =>
            {
                var gatewayOptions = sp.GetRequiredService<IOptions<Aqsat.Infrastructure.Payments.ZarinPalOptions>>().Value;
                http.BaseAddress = new Uri(gatewayOptions.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(gatewayOptions.TimeoutSeconds);
            })
            .AddStandardResilienceHandler();
        services.AddScoped<Aqsat.Application.Payments.IPaymentGateway>(
            sp => sp.GetRequiredService<Aqsat.Infrastructure.Payments.ZarinPalPaymentGateway>());
        services.AddScoped<Aqsat.Infrastructure.Portal.PortalInvitationService>();
        services.AddScoped<Aqsat.Infrastructure.Portal.PolicyVerificationService>();
        services.AddScoped<Aqsat.Infrastructure.Portal.InstallmentPaymentLinkService>();
        services.AddScoped<Aqsat.Infrastructure.Customers.CustomerCreationService>();
        services.AddScoped<Aqsat.Infrastructure.Risk.RiskFeatureCalculator>();
        services.AddScoped<Aqsat.Infrastructure.Risk.RiskAssessmentService>();
        services.AddScoped<Aqsat.Infrastructure.Risk.RiskQueryService>();
        services.AddScoped<Aqsat.Infrastructure.Risk.NetworkRiskQueryService>();
        services.AddScoped<Aqsat.Infrastructure.Risk.RiskReviewService>();
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
