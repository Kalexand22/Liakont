namespace Liakont.Modules.TenantSettings.Infrastructure;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TenantSettings.Infrastructure.Queries;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module TenantSettings (profil tenant, fiscal, comptes PA chiffrés,
/// planification, seuils, import de seed).
/// </summary>
public static class TenantSettingsModuleRegistration
{
    public static IServiceCollection AddTenantSettingsModule(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(ITenantSettingsApplicationMarker).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(TenantSettingsModuleRegistration).Assembly);
        });

        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(TenantSettingsModuleRegistration).Assembly));

        // Chiffrement des secrets PA : ASP.NET Core Data Protection. Le nom d'application est
        // fixé ici pour que le discriminant de clé soit stable quel que soit le content-root du
        // conteneur. IMPORTANT : le STORE de persistance des clés (par instance/appliance) DOIT
        // être configuré par OPS01 en production — sans cela, les clés sont éphémères et chaque
        // clé API PA chiffrée devient indéchiffrable après un redémarrage de l'instance.
        services.AddDataProtection().SetApplicationName("Liakont");

        services.AddScoped<ITenantSettingsUnitOfWorkFactory, PostgresTenantSettingsUnitOfWorkFactory>();
        services.AddScoped<ITenantSettingsQueries, PostgresTenantSettingsQueries>();

        // Lecture SÉGRÉGÉE de la matrice de routage des alertes (FIX212, F12 §5.3.1) : consommée par le
        // routage des notifications (Supervision) et la page de paramétrage, sans imposer la méthode aux
        // nombreux implémenteurs d'ITenantSettingsQueries (tests inclus).
        services.AddScoped<IAlertRoutingQueries, PostgresAlertRoutingQueries>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        // Lecture chiffrée des secrets d'un compte PA actif (résolution OAuth2 par un résolveur de plug-in
        // côté Host). Scoped : la connexion est routée vers la base du tenant courant.
        services.AddScoped<IPaAccountSecretStore, PostgresPaAccountSecretStore>();
        services.AddScoped<TenantSettingsJournal>();

        // Lecture composée du paramétrage pour la console (API01c, GET /api/v1/settings) : assemble
        // profil/fiscal/comptes PA (du module) + état TVA + capacités PA (via leurs Contracts).
        services.AddScoped<ITenantSettingsConsoleQueries, TenantSettingsConsoleQueries>();

        return services;
    }
}
