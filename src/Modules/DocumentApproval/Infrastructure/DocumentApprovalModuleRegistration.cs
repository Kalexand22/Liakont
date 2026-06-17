namespace Liakont.Modules.DocumentApproval.Infrastructure;

using Liakont.Modules.DocumentApproval.Application;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Contracts.Queries;
using Liakont.Modules.DocumentApproval.Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module DocumentApproval (workflow de validation de document générique — ADR-0028,
/// F17 §3/§4) : migrations DbUp (schéma <c>documentapproval</c> + journal append-only), fabrique d'unités de
/// travail, requêtes de lecture, et — depuis SIG06 — le câblage de la Règle de gate (<c>IDocumentApprovalGate</c>)
/// + le paramétrage tenant du niveau requis (<c>IDocumentApprovalRequirements</c>, V005).
/// </summary>
public static class DocumentApprovalModuleRegistration
{
    public static IServiceCollection AddDocumentApprovalModule(this IServiceCollection services)
    {
        // Ajoute l'assembly du module à la liste globale que DbUp scanne au démarrage et au provisioning d'un
        // tenant (les migrations V001-V004 sont tenant-scopées).
        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(DocumentApprovalModuleRegistration).Assembly));

        services.AddScoped<IDocumentValidationUnitOfWorkFactory, PostgresDocumentValidationUnitOfWorkFactory>();
        services.AddScoped<IDocumentApprovalQueries, PostgresDocumentApprovalQueries>();

        // Port de commande générique (SIG05) : pilote le cycle de vie d'une validation pour un module exposeur
        // (Mandats, etc.) via la frontière Contracts (jamais l'UoW Application directement).
        services.AddScoped<IDocumentApprovalWorkflow, DocumentApprovalWorkflow>();

        // SIG06 — Règle de gate câblée de bout en bout (ADR-0028 §5, INV-APPROVAL-4) : statue sur l'émissibilité
        // d'un document (état × niveau de preuve requis × forme expresse self-billing). Les ports de purpose
        // (ISelfBilledGate, ICreditNoteAcceptanceGate, IMandateSignatureGate, IMultiPartySignatureGate) délèguent
        // à ce port générique. Le niveau requis est un PARAMÉTRAGE TENANT (défaut Recorded), jamais un défaut
        // produit (CLAUDE.md n°2/3).
        services.AddScoped<IDocumentApprovalGate, DocumentApprovalGate>();
        services.AddScoped<IDocumentApprovalRequirements, PostgresDocumentApprovalRequirements>();

        return services;
    }
}
