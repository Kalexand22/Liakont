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
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<TenantSettingsJournal>();

        return services;
    }
}
