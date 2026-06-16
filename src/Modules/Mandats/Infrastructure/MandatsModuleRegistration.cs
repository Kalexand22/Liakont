namespace Liakont.Modules.Mandats.Infrastructure;

using Liakont.Modules.Mandats.Application;
using Liakont.Modules.Mandats.Contracts;
using Liakont.Modules.Mandats.Contracts.Queries;
using Liakont.Modules.Mandats.Infrastructure.Queries;
using Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // Acceptation des auto-factures sous mandat (MND02, ADR-0024) PROJETÉE via DocumentApproval (SIG05) :
        // le cycle de vie (commandes) délègue l'état + le journal append-only à DocumentApproval ; les lectures
        // composent l'état projeté (DocumentApproval) et la companion fiscale (BT-1 / pending_since).
        services.AddScoped<ISelfBilledAcceptanceCommands, SelfBilledAcceptanceCommands>();
        services.AddScoped<ISelfBilledAcceptanceQueries, PostgresSelfBilledAcceptanceQueries>();

        // Garde d'émission interrogée par le pipeline avant l'envoi (MND03, ADR-0024 §3 / INV-ACCEPT-2).
        services.AddScoped<ISelfBilledGate, SelfBilledGate>();

        // Allocateur du BT-1 fiscal 389 (MND05, ADR-0025) : get-or-create idempotent sur la clé source, verrou
        // par mandant ; interrogé par le pipeline au plus tard avant l'envoi, après acceptation.
        services.AddScoped<ISelfBilledNumberAllocator, PostgresSelfBilledNumberAllocator>();

        // Bascule tacite des acceptations 389 (MND04, ADR-0024 §4) : service de bascule résolu par le TenantJob
        // (SOL06) dans le scope du tenant. Depuis SIG05, candidats dus et transition sont lus/pilotés via
        // DocumentApproval (purpose SelfBilledAcceptance). Horloge injectable (défaut système) pour la condition
        // « now ≥ DeadlineUtc » — testable, jamais DateTimeOffset.UtcNow en dur.
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ITacitAcceptanceService, TacitAcceptanceService>();

        return services;
    }
}
