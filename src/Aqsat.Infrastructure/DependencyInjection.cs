using Aqsat.Application.ApiIr;
using Aqsat.Application.Common;
using Aqsat.Application.Concurrency;
using Aqsat.Application.Schedule;
using Aqsat.Application.Sms;
using Aqsat.Infrastructure.ApiIr;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Concurrency;
using Aqsat.Infrastructure.Import;
using Aqsat.Infrastructure.Jobs;
using Aqsat.Infrastructure.Persistence;
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
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
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
        services.AddScoped<IHolidayChecker, ApiIrHolidayChecker>();

        services.AddScoped<ILockService, RecordLockService>();
        services.AddSingleton<PresenceConnectionRegistry>();
        services.AddScoped<IPresenceService, PresenceService>();

        services.AddScoped<DeadlineRecalculationJob>();
        services.AddScoped<PresenceAndLockSweepJob>();
        services.AddScoped<SmsReminderJob>();
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(configuration.GetConnectionString("Default")));
        services.AddHangfireServer();

        return services;
    }
}
