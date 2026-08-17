using Aqsat.Application.Common;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Security;
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

        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseSqlServer(configuration.GetConnectionString("Default"))
            .AddInterceptors(sp.GetRequiredService<AgencySessionContextInterceptor>()));

        return services;
    }
}
