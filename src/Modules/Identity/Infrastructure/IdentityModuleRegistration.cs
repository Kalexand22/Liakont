namespace Stratum.Modules.Identity.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Application.Preferences;
using Stratum.Modules.Identity.Contracts.Queries;
using Stratum.Modules.Identity.Domain.Repositories;
using Stratum.Modules.Identity.Infrastructure.Queries;
using Stratum.Modules.Identity.Infrastructure.Repositories;
using Stratum.Modules.Identity.Infrastructure.Security;
using Stratum.Modules.Identity.Infrastructure.Services;

public static class IdentityModuleRegistration
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(IIdentityApplicationMarker).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(IdentityModuleRegistration).Assembly);
        });

        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(IdentityModuleRegistration).Assembly));

        services.Configure<UserSyncOptions>(configuration.GetSection(UserSyncOptions.SectionName));

        services.AddHostedService<IdentityEventTypeRegistrar>();

        services.AddScoped<IUserRepository, PostgresUserRepository>();
        services.AddScoped<IRoleRepository, PostgresRoleRepository>();
        services.AddScoped<IGrantRepository, PostgresGrantRepository>();
        services.AddScoped<IIdentityQueries, PostgresIdentityQueries>();
        services.AddScoped<IAgentQueries, PostgresAgentQueries>();
        services.AddScoped<ITeamQueries, PostgresTeamQueries>();
        services.AddScoped<IDelegationQueries, PostgresDelegationQueries>();
        services.AddScoped<IIdentityUnitOfWorkFactory, PostgresIdentityUnitOfWorkFactory>();
        services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();
        services.AddSingleton<IPermissionCatalog, ReflectionPermissionCatalog>();
        services.AddScoped<IUserSyncService, UserSyncService>();
        services.AddScoped<IUserPreferencesService, PostgresUserPreferencesService>();

        return services;
    }
}
