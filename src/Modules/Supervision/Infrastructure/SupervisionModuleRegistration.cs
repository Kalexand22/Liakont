namespace Liakont.Modules.Supervision.Infrastructure;

using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Application.Rules;
using Liakont.Modules.Supervision.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module Supervision (F12 §5, items SUP01a/SUP01b) : migrations DbUp (schéma
/// <c>supervision</c> + table des alertes), store et lectures des alertes, service d'acquittement, le
/// MOTEUR d'évaluation (anti-bruit / auto-résolution) et les <see cref="IAlertRule"/> concrètes (SUP01b).
/// Le moteur dispatche sur les règles enregistrées ; chaque règle lit sa source de données par le Contract
/// du module propriétaire (Ingestion, Documents, TenantSettings) dans le scope tenant du dead-man's-switch.
/// Le handler de fan-out du dead-man's-switch (job SYSTÈME) est enregistré par le Host (composition root,
/// via <c>AddJobHandler</c>), comme les autres job handlers.
/// </summary>
public static class SupervisionModuleRegistration
{
    public static IServiceCollection AddSupervisionModule(this IServiceCollection services)
    {
        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(SupervisionModuleRegistration).Assembly));

        // Persistance des alertes (base du tenant) + lectures (dashboard SUP02) + acquittement opérateur.
        services.AddScoped<IAlertStore, PostgresAlertStore>();
        services.AddScoped<IAlertQueries, PostgresAlertQueries>();

        // Acquittement : horloge partagée si enregistrée (sinon horloge système) — horodatage déterministe en test.
        services.AddScoped<IAlertAcknowledgementService>(sp => new AlertAcknowledgementService(
            sp.GetRequiredService<IAlertStore>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));

        // Moteur d'évaluation : dispatche sur toutes les IAlertRule enregistrées.
        // Fabrique explicite pour lever toute ambiguïté de constructeur et utiliser l'horloge partagée.
        services.AddScoped<IAlertEvaluationService>(sp => new AlertEvaluationService(
            sp.GetServices<IAlertRule>(),
            sp.GetRequiredService<IAlertStore>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));

        // Règles d'alerte constructibles (SUP01b, F12 §5.2) — chacune lit une donnée DÉJÀ persistée et
        // tenant-scopée via le Contract du module propriétaire, résolu dans le scope tenant du job. Les 5
        // autres règles (run manqué, file de push, SIREN/tax_report PA, version d'agent, échéance J-3) sont
        // gelées en SUP01c (producteurs de données manquants).
        services.AddScoped<IAlertRule, AgentMuteAlertRule>();
        services.AddScoped<IAlertRule, BlockedDocumentsAlertRule>();
        services.AddScoped<IAlertRule, PaRejectedDocumentsAlertRule>();

        return services;
    }
}
