namespace Liakont.Host.Documents;

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Liakont.Host.Security;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Ingestion.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Endpoint console de consultation du PDF D'ORIGINE d'un document (le bordereau poussé par l'agent avec
/// le document, stocké par le module Ingestion — jamais un rendu généré). Consommé par le lien « pièce
/// jointe » de la fiche document : le téléchargement de fichier passe par un endpoint HTTP (même schéma
/// que l'export de réversibilité de <c>ClientExportEndpoint</c> — « actions in-process » vise les
/// commandes, pas les flux binaires).
/// <para>
/// Endpoint du HOST (composition root) et non du module Documents : il compose DEUX modules — la lecture
/// du document (Documents, qui fait foi de l'appartenance au tenant : la connexion EST le tenant) et le
/// stockage des PDF ingérés (Ingestion) — qu'un module ne peut pas référencer l'un l'autre hors Contracts.
/// Servi en <c>inline</c> (consultation dans le navigateur), nom de fichier dérivé du n° de document.
/// </para>
/// </summary>
public static class DocumentSourcePdfEndpoint
{
    public static IEndpointRouteBuilder MapDocumentSourcePdfEndpoint(this IEndpointRouteBuilder app)
    {
        // GET /api/v1/documents/{id}/piece-jointe — flux du PDF d'origine, 404 français sinon.
        app.MapGet("/documents/{id:guid}/piece-jointe", (
            Guid id,
            IDocumentQueries queries,
            IIngestedPdfStore pdfStore,
            ITenantContext tenantContext,
            HttpContext context) => HandleAsync(id, queries, pdfStore, tenantContext, context))
            .RequireAuthorization(LiakontPermissions.Read);

        return app;
    }

    /// <summary>Corps du handler, extrait pour être testé unitairement branche par branche (les 404 + le flux).</summary>
    internal static async Task<IResult> HandleAsync(
        Guid id,
        IDocumentQueries queries,
        IIngestedPdfStore pdfStore,
        ITenantContext tenantContext,
        HttpContext context)
    {
        // Le stockage Ingestion est adressé par tenant : sans tenant résolu (session super-admin
        // cross-tenant), il n'y a aucun stockage à consulter — la fiche ne propose alors pas le lien.
        var tenantId = tenantContext.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.NotFound("Aucun tenant courant : la pièce jointe d'un document se consulte depuis la console du tenant.");
        }

        // Lecture tenant-scopée par construction (la connexion EST le tenant) : un identifiant d'un
        // autre tenant est introuvable ici — pas de fuite cross-tenant possible via l'URL.
        var document = await queries.GetByIdAsync(id, context.RequestAborted);
        if (document is null)
        {
            return Results.NotFound("Document introuvable sur ce tenant.");
        }

        Stream? stream = await pdfStore.TryOpenLinkedPdfAsync(tenantId, document.SourceReference, context.RequestAborted);
        if (stream is null)
        {
            return Results.NotFound(
                $"Aucune pièce jointe reçue pour le document « {document.DocumentNumber} » : la source n'a pas fourni de PDF (cas normal), ou l'agent ne l'a pas encore transmis.");
        }

        // inline + nom de fichier lisible : consultation dans le navigateur, enregistrement sous un
        // nom parlant (jamais le hash de stockage). Results.File avec fileDownloadName forcerait
        // « attachment » : l'en-tête est posé explicitement. nosniff (défense en profondeur) : les octets
        // viennent du poste de l'agent — un navigateur ne doit JAMAIS re-deviner le type d'un fichier
        // non-PDF poussé sous ce nom (contenu actif exécuté dans l'origine de la console sinon).
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.ContentDisposition =
            $"inline; filename=\"{SafeFileName(document.DocumentNumber)}.pdf\"";
        return Results.File(stream, "application/pdf");
    }

    /// <summary>Réduit le n° de document au jeu sûr pour un nom de fichier d'en-tête (anti-injection d'en-tête).</summary>
    private static string SafeFileName(string documentNumber)
    {
        var builder = new StringBuilder(documentNumber.Length);
        foreach (char c in documentNumber)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-');
        }

        var safe = builder.ToString();
        return string.IsNullOrEmpty(safe) ? "document" : safe;
    }
}
