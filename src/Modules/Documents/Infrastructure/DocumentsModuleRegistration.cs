namespace Liakont.Modules.Documents.Infrastructure;

using Liakont.Modules.Documents.Application;
using Liakont.Modules.Documents.Contracts.Deduplication;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Documents.Infrastructure.Deduplication;
using Liakont.Modules.Documents.Infrastructure.Lookups;
using Liakont.Modules.Documents.Infrastructure.Queries;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Ingestion.Contracts.Events;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Contracts.CreditNotes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stratum.Common.Abstractions.Events;
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

        // Anti-doublon AVANT envoi (TRK03, F06 §4) — port consommé par le pipeline (PIP01).
        services.AddScoped<IDuplicateDocumentCheck, PostgresDuplicateDocumentCheck>();

        // Implémentations réelles (TRK03) des ports d'unicité (VAL03) et d'avoirs (VAL04) déclarés par le
        // module Validation, branchées sur le repository des documents émis du tenant. Aucun défaut de
        // production n'existe ailleurs : le module Documents est leur unique fournisseur.
        services.AddScoped<IIssuedDocumentLookup, IssuedDocumentLookup>();
        services.AddScoped<IIssuedInvoiceLookup, IssuedInvoiceLookup>();

        // Consommateur de l'altération source après émission (TRK03) : inscrit un fait d'audit append-only
        // sur le document émis, jamais de réémission. L'événement est produit par l'ingestion (PIV04) ;
        // le worker d'outbox du socle dispatche ce consommateur.
        services.AddScoped<IIntegrationEventConsumer<SourceAlterationDetectedV1>, SourceAlterationDetectedConsumer>();

        // Port de création du document en état Detected (PIV04). Replace écrase le défaut sûr NoOpDocumentIntake ;
        // combiné au TryAdd du module Ingestion, le câblage est indépendant de l'ordre d'enregistrement des
        // modules : la vraie implémentation (DocumentIntake) gagne toujours (et le no-op sert côté Ingestion
        // seul, hors plateforme).
        services.Replace(ServiceDescriptor.Scoped<IDocumentIntake, DocumentIntake>());

        return services;
    }
}
