namespace Liakont.Modules.Pipeline.Infrastructure;

using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Liakont.Modules.Pipeline.Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module Pipeline (PIP01a — fondations). Enrôle les migrations DbUp (la table
/// <c>pipeline.run_logs</c> est CRÉÉE ici, écrite par PIP01b+), ancre MediatR (handlers CHECK/SEND/SYNC
/// à venir) et expose la lecture du journal d'exécutions. AUCUN comportement de pipeline ici.
/// </summary>
public static class PipelineModuleRegistration
{
    /// <summary>Enregistre le module Pipeline (migrations + ancre MediatR + lecture du journal d'exécutions).</summary>
    /// <param name="services">La collection de services.</param>
    /// <returns>La collection de services, pour chaînage.</returns>
    public static IServiceCollection AddPipelineModule(this IServiceCollection services)
    {
        // Ancre MediatR du module : les handlers (CHECK/SEND/SYNC) arrivent avec PIP01b+. L'enregistrement
        // est fait dès maintenant pour rester homogène avec les autres modules.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(IPipelineApplicationMarker).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(PipelineModuleRegistration).Assembly);
        });

        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(PipelineModuleRegistration).Assembly));

        services.AddScoped<IPipelineRunQueries, PostgresPipelineRunQueries>();

        return services;
    }
}
