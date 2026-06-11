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

        // Lectures AGRÉGÉES cross-tenant du dashboard (SUP02) : énumère les tenants (registre système) et
        // agrège tenant par tenant via ITenantScopeFactory — l'UNIQUE lecture cross-tenant du produit
        // (CLAUDE.md n°9). L'acquittement d'alerte est routé dans le scope du tenant concerné.
        services.AddScoped<ISupervisionDashboardQueries, CrossTenantSupervisionDashboardQueries>();

        // Lecture du DISPOSITIF d'alerte du tenant courant (FIX210) : règles actives/gelées (F12 §5.2) +
        // seuils effectifs (CFG02) + état e-mail opérateur (F12 §5.3). Tenant-scopée, sans secret exposé.
        services.AddScoped<IAlertDeviceQueries, AlertDeviceQueries>();

        // Lecture de la dernière évaluation du dead-man's-switch (FIX210, F12 §5.1) : dernier achèvement du job
        // SYSTÈME d'évaluation, via le Contract Job. Résolue dans un scope SYSTÈME par le témoin de vie (Host).
        services.AddScoped<ISupervisionLivenessQueries, SupervisionLivenessQueries>();

        // Acquittement : horloge partagée si enregistrée (sinon horloge système) — horodatage déterministe en test.
        services.AddScoped<IAlertAcknowledgementService>(sp => new AlertAcknowledgementService(
            sp.GetRequiredService<IAlertStore>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));

        // Notification des alertes par email (SUP03, F12 §5.3) : un seul AlertEmailNotifier scoped sert les
        // DEUX abstractions (transitions d'alerte + digest). Tenant-scopé par construction (lectures
        // TenantSettings/Alert + mise en file via la connexion du tenant). Ne porte AUCUN secret (le mot de
        // passe SMTP vit dans le transport SMTP, côté Host). Le contenu/destinataires sont composés ici ;
        // l'envoi réel (et son retry) est porté par le pipeline de jobs du module Notification.
        services.AddScoped<AlertEmailNotifier>();
        services.AddScoped<IAlertNotifier>(sp => sp.GetRequiredService<AlertEmailNotifier>());
        services.AddScoped<IAlertDigestSender>(sp => sp.GetRequiredService<AlertEmailNotifier>());

        // Moteur d'évaluation : dispatche sur toutes les IAlertRule enregistrées.
        // Fabrique explicite pour lever toute ambiguïté de constructeur et utiliser l'horloge partagée + le notifieur.
        services.AddScoped<IAlertEvaluationService>(sp => new AlertEvaluationService(
            sp.GetServices<IAlertRule>(),
            sp.GetRequiredService<IAlertStore>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            sp.GetRequiredService<IAlertNotifier>()));

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
