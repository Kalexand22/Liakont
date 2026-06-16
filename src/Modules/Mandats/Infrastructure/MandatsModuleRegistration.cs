namespace Liakont.Modules.Mandats.Infrastructure;

using Liakont.Modules.Mandats.Application;
using Liakont.Modules.Mandats.Contracts;
using Liakont.Modules.Mandats.Contracts.Queries;
using Liakont.Modules.Mandats.Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module Mandats (registre des mandants + cycle de vie des mandats — F15 §2,
/// ADR-0022 ; workflow d'acceptation des auto-factures 389 — F15 §2.3, ADR-0024, MND02) : migrations DbUp
/// (schéma <c>mandats</c> + journaux append-only), fabriques d'unités de travail et requêtes de lecture.
/// </summary>
public static class MandatsModuleRegistration
{
    public static IServiceCollection AddMandatsModule(this IServiceCollection services)
    {
        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(MandatsModuleRegistration).Assembly));

        services.AddScoped<IMandatsUnitOfWorkFactory, PostgresMandatsUnitOfWorkFactory>();
        services.AddScoped<IMandatsQueries, PostgresMandatsQueries>();

        // Acceptation des auto-factures sous mandat (MND02, ADR-0024).
        services.AddScoped<ISelfBilledAcceptanceUnitOfWorkFactory, PostgresSelfBilledAcceptanceUnitOfWorkFactory>();
        services.AddScoped<ISelfBilledAcceptanceQueries, PostgresSelfBilledAcceptanceQueries>();

        // Garde d'émission interrogée par le pipeline avant l'envoi (MND03, ADR-0024 §3 / INV-ACCEPT-2).
        services.AddScoped<ISelfBilledGate, SelfBilledGate>();

        return services;
    }
}
