using Aqsat.Application.Common;
using Aqsat.Application.Concurrency;
using Aqsat.Application.Schedule;
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

        services.AddSingleton<IHolidayChecker, WeekendOnlyHolidayChecker>();
        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseSqlServer(configuration.GetConnectionString("Default"))
            .AddInterceptors(sp.GetRequiredService<AgencySessionContextInterceptor>()));

        services.AddScoped<ILockService, RecordLockService>();
        services.AddSingleton<PresenceConnectionRegistry>();
        services.AddScoped<IPresenceService, PresenceService>();

        services.AddScoped<DeadlineRecalculationJob>();
        services.AddScoped<PresenceAndLockSweepJob>();
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(configuration.GetConnectionString("Default")));
        services.AddHangfireServer();

        return services;
    }
}
