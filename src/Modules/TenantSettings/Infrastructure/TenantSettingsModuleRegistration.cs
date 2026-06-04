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

        // Chiffrement des secrets PA : ASP.NET Core Data Protection. Garantit la disponibilité du
        // provider. La PERSISTANCE des clés de protection (par instance/appliance) et le nom
        // d'application stable sont configurés au niveau hôte par OPS01 (F12-A §4) — pas ici, pour
        // ne pas qu'un module dicte une décision d'instance.
        services.AddDataProtection();

        services.AddScoped<ITenantSettingsUnitOfWorkFactory, PostgresTenantSettingsUnitOfWorkFactory>();
        services.AddScoped<ITenantSettingsQueries, PostgresTenantSettingsQueries>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<TenantSettingsJournal>();

        return services;
    }
}
