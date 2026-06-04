namespace Liakont.Modules.TvaMapping.Infrastructure;

using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Contracts.Queries;
using Liakont.Modules.TvaMapping.Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module TvaMapping (table de mapping TVA par tenant — F03, item TVA01) :
/// migrations DbUp, fabrique d'unités de travail et requêtes de lecture.
/// </summary>
public static class TvaMappingModuleRegistration
{
    public static IServiceCollection AddTvaMappingModule(this IServiceCollection services)
    {
        // Anchor MediatR du module : les handlers de commande (édition) et de requête arrivent avec
        // le moteur de mapping (TVA02) et l'édition console (TVA05). L'enregistrement du pipeline est
        // fait dès maintenant pour rester homogène avec les autres modules.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(ITvaMappingApplicationMarker).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(TvaMappingModuleRegistration).Assembly);
        });

        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(TvaMappingModuleRegistration).Assembly));

        services.AddScoped<ITvaMappingUnitOfWorkFactory, PostgresTvaMappingUnitOfWorkFactory>();
        services.AddScoped<ITvaMappingQueries, PostgresTvaMappingQueries>();

        return services;
    }
}
