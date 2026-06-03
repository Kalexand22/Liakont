namespace Stratum.Common.Infrastructure.Audit;

using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Audit;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStratumAudit(this IServiceCollection services)
    {
        // AuditWriter is Singleton-safe: it only holds IConnectionFactory (also Singleton)
        // and opens a new IDbConnection per call — no shared mutable state.
        services.AddSingleton<IAuditWriter, AuditWriter>();

        // ActivityLogger follows the same Singleton pattern as AuditWriter.
        services.AddSingleton<IActivityLogger, ActivityLogger>();

        return services;
    }
}
