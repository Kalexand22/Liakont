namespace Liakont.Modules.Ged.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module GED (F19 §2.1, scaffold GED02). À ce stade, l'unique effet est de déclarer
/// l'assembly d'Infrastructure au runner de migrations DbUp (<see cref="MigrationAssembliesOptions"/>) afin
/// que ses scripts <c>Migrations/*.sql</c> soient appliqués :
/// <list type="bullet">
/// <item><description><c>ged_catalog</c> + <c>ged_index</c> : indexation métier, vivent dans la base DU
/// TENANT (isolation = la connexion, <c>IConnectionFactory</c> ; F19 §3.2).</description></item>
/// <item><description><c>ged_ingestion</c> : registre d'ingestion GED, vit dans la base SYSTÈME
/// (co-localisé avec l'outbox pour écrire ATOMIQUEMENT le registre + l'événement, comme
/// <c>ingestion.received_documents</c> ; écrit via <c>ISystemConnectionFactory</c> à partir de GED05b ;
/// F19 §3.2 exception (a)).</description></item>
/// </list>
/// Les schémas sont créés VIDES ici (aucune table métier) ; les entités du méta-modèle arrivent avec
/// GED03a/GED03b/GED03c. Les stores, l'UoW, le mapping, la recherche et les jobs seront enregistrés par les
/// items suivants. Le module ne référence aucun autre module métier (frontière F19 §7 : le flux fiscal
/// IGNORE la GED).
/// </summary>
public static class GedModuleRegistration
{
    public static IServiceCollection AddGedModule(this IServiceCollection services)
    {
        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(GedModuleRegistration).Assembly));

        return services;
    }
}
