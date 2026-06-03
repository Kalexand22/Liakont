namespace Stratum.Modules.Audit.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Audit.Application;
using Stratum.Modules.Audit.Contracts.Queries;
using Stratum.Modules.Audit.Domain.Repositories;
using Stratum.Modules.Audit.Infrastructure.Queries;
using Stratum.Modules.Audit.Infrastructure.Repositories;

public static class AuditModuleRegistration
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(IAuditApplicationMarker).Assembly);
        });

        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(AuditModuleRegistration).Assembly));

        services.AddScoped<IAuditPolicyRepository, PostgresAuditPolicyRepository>();
        services.AddScoped<IAuditQueries, PostgresAuditQueries>();

        return services;
    }
}
