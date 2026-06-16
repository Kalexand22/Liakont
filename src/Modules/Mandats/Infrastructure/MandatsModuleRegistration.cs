namespace Liakont.Modules.Mandats.Infrastructure;

using Liakont.Modules.Mandats.Application;
using Liakont.Modules.Mandats.Contracts.Queries;
using Liakont.Modules.Mandats.Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module Mandats (registre des mandants + cycle de vie des mandats — F15 §2,
/// ADR-0022) : migrations DbUp (schéma <c>mandats</c> + journal append-only), fabrique d'unités de travail
/// et requêtes de lecture. MND01 (fondation) n'enregistre aucun handler MediatR — ils arrivent avec MND02+.
/// </summary>
public static class MandatsModuleRegistration
{
    public static IServiceCollection AddMandatsModule(this IServiceCollection services)
    {
        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(MandatsModuleRegistration).Assembly));

        services.AddScoped<IMandatsUnitOfWorkFactory, PostgresMandatsUnitOfWorkFactory>();
        services.AddScoped<IMandatsQueries, PostgresMandatsQueries>();

        return services;
    }
}
