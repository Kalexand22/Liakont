namespace Liakont.Modules.DocumentApproval.Infrastructure;

using Liakont.Modules.DocumentApproval.Application;
using Liakont.Modules.DocumentApproval.Contracts.Queries;
using Liakont.Modules.DocumentApproval.Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module DocumentApproval (workflow de validation de document générique — ADR-0028,
/// F17 §3/§4) : migrations DbUp (schéma <c>documentapproval</c> + journal append-only), fabrique d'unités de
/// travail et requêtes de lecture. SIG04 (cœur générique) n'enregistre AUCUN port par purpose ni job — ils
/// arrivent avec SIG06/SIG07.
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

        return services;
    }
}
