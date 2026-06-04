namespace Liakont.Modules.Documents.Infrastructure;

using Liakont.Modules.Documents.Application;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Documents.Infrastructure.Queries;
using Liakont.Modules.Ingestion.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module Documents (F06, item TRK01) : migrations DbUp, fabrique d'unités de
/// travail, requêtes de lecture, et port de création du document <c>Detected</c> consommé par l'ingestion.
/// </summary>
public static class DocumentsModuleRegistration
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // Anchor MediatR du module : les handlers (machine à états TRK02, anti-doublon TRK03, audit TRK04)
        // arrivent ensuite. L'enregistrement du pipeline est fait dès maintenant pour rester homogène
        // avec les autres modules.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(IDocumentsApplicationMarker).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(DocumentsModuleRegistration).Assembly);
        });

        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(DocumentsModuleRegistration).Assembly));

        services.AddScoped<IDocumentUnitOfWorkFactory, PostgresDocumentUnitOfWorkFactory>();
        services.AddScoped<IDocumentQueries, PostgresDocumentQueries>();

        // Port de création du document en état Detected (PIV04). Remplace le défaut SÛR NoOpDocumentIntake
        // enregistré par le module Ingestion : Replace garantit que la vraie implémentation gagne quel que
        // soit l'ordre d'enregistrement des modules (et reste un no-op côté Ingestion seul, hors plateforme).
        services.Replace(ServiceDescriptor.Scoped<IDocumentIntake, DocumentIntake>());

        return services;
    }
}
