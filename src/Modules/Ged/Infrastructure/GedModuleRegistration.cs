namespace Liakont.Modules.Ged.Infrastructure;

using Liakont.Modules.Ged.Application;
using Liakont.Modules.Ged.Application.Graph;
using Liakont.Modules.Ged.Application.Mapping;
using Liakont.Modules.Ged.Contracts.Consultation;
using Liakont.Modules.Ged.Infrastructure.Consultation;
using Liakont.Modules.Ged.Infrastructure.Graph;
using Liakont.Modules.Ged.Infrastructure.Mapping;
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
        // Handlers MediatR (Application + Infrastructure) — SetAxisValueCommandHandler (GED04) vit en Infrastructure
        // car il orchestre l'accès base (IAxisCatalog + UoW Dapper), comme F19 §3.7.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(IGedApplicationMarker).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(GedModuleRegistration).Assembly);
        });

        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(GedModuleRegistration).Assembly));

        // Écriture de l'index GED (GED04) : catalogue d'axes (lecture) + UoW transactionnelle Dapper (garde de
        // concurrence mono-valeur RL-02). Scopés (tenant = la connexion).
        services.AddScoped<IAxisCatalog, PostgresAxisCatalog>();
        services.AddScoped<IGedIndexUnitOfWorkFactory, PostgresGedIndexUnitOfWorkFactory>();

        // Lecture des profils de mapping GED validés (GED12, F19 §4.5), tenant-scopée par IConnectionFactory.
        // Surface consommée par le consommateur d'ingestion GED (GED05b) : pour chaque document ingéré, il
        // charge le profil VALIDÉ de son documentType et applique GedMapper (mappé) ou range en `deferred`.
        services.AddScoped<IGedMappingProfileStore, GedMappingProfileRepository>();

        // Journal de consultation GED append-only (GED13, F19 §6.6, ADR-0036), tenant-scopé par IConnectionFactory
        // (JAMAIS ISystemConnectionFactory). Le seam de régime (best-effort par défaut / probant activable) résout la
        // capacité tenant ; l'implémentation par défaut renvoie BestEffort (D8 non tranché). CONSOMMÉ par les pages
        // /ged/* (GED08/GED09) pour journaliser recherche, fiche, exploration, export et ouverture de paquet.
        services.AddScoped<IConsultationAuditModeProvider, DefaultConsultationAuditModeProvider>();
        services.AddScoped<IConsultationAuditWriter, PostgresConsultationAuditWriter>();

        // Inférence/héritage borné des relations entité↔entité (GED24, F19 §10), tenant-scopé par IConnectionFactory :
        // lecture des règles tenant + du voisinage asserté borné (anti-DoS). Le handler (InferEntityRelationsCommand)
        // est découvert via AddMediatR ci-dessus ; il matérialise les relations dérivées append-only via l'UoW.
        services.AddScoped<IRelationInferenceRuleStore, PostgresRelationInferenceRuleStore>();
        services.AddScoped<IEntityRelationGraphReader, PostgresEntityRelationGraphReader>();

        return services;
    }
}
