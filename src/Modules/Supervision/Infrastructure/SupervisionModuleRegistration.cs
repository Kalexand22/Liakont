namespace Liakont.Modules.Supervision.Infrastructure;

using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module Supervision (F12 §5, item SUP01a) : migrations DbUp (schéma
/// <c>supervision</c> + table des alertes), store et lectures des alertes, service d'acquittement, et le
/// MOTEUR d'évaluation (anti-bruit / auto-résolution). Le moteur dispatche sur les <see cref="IAlertRule"/>
/// enregistrées — AUCUNE règle concrète ici (SUP01b les ajoute) : sans règle, l'évaluation ne produit
/// aucune alerte. Le handler de fan-out du dead-man's-switch (job SYSTÈME) est enregistré par le Host
/// (composition root, via <c>AddJobHandler</c>), comme les autres job handlers.
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

        // Moteur d'évaluation : dispatche sur toutes les IAlertRule enregistrées (aucune en SUP01a).
        // Fabrique explicite pour lever toute ambiguïté de constructeur et utiliser l'horloge partagée.
        services.AddScoped<IAlertEvaluationService>(sp => new AlertEvaluationService(
            sp.GetServices<IAlertRule>(),
            sp.GetRequiredService<IAlertStore>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));

        return services;
    }
}
