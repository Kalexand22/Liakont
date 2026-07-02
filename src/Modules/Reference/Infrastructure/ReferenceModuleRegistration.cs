namespace Liakont.Modules.Reference.Infrastructure;

using Liakont.Modules.Reference.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module Reference (ADR-0038) : référentiel de correspondance pays cross-instance
/// (table système, journal append-only, normalisation read-time). Home NEUTRE — PAS Supervision (qui est
/// tenant-scopé en lecture seule) — pour ne pas inverser la relation d'observation ni diluer l'invariant
/// « Supervision = read-only cross-tenant » (CLAUDE.md n°9). Le Pipeline le consomme via Contracts uniquement.
/// </summary>
public static class ReferenceModuleRegistration
{
    public static IServiceCollection AddReferenceModule(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ReferenceModuleRegistration).Assembly));

        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(ReferenceModuleRegistration).Assembly));

        // Singleton : la résolution (chemin chaud CHECK/SEND) est servie depuis un cache mémoire invalidé à
        // chaque écriture admin (petit volume, table universelle cross-instance). Le même singleton porte la
        // lecture (ICountryAliasReferential) ET l'écriture (upsert/remove journalisés) — cache cohérent.
        services.AddSingleton<PostgresCountryAliasReferential>();
        services.AddSingleton<ICountryAliasReferential>(sp => sp.GetRequiredService<PostgresCountryAliasReferential>());

        return services;
    }
}
