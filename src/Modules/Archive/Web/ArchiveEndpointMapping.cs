namespace Liakont.Modules.Archive.Web;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Endpoints d'export d'audit / contrôle fiscal et de réversibilité du module Archive pour la console
/// (API03 ; TRK05/TRK06, F06 §7, F12 §6.3), montés sous <c>/api/v1</c> par le Host. Toutes les lectures
/// sont TENANT-SCOPÉES par construction (la connexion EST le tenant — database-per-tenant, blueprint §7 ;
/// CLAUDE.md n°9/17). Aucune logique métier ici : les endpoints délèguent aux services du module
/// (<see cref="IFiscalControlExportService"/>, <see cref="ITenantReversibilityExportService"/>,
/// <see cref="IArchiveVerifier"/>) et écrivent le résultat en archive ZIP directement dans le corps de
/// réponse, FICHIER PAR FICHIER lu PARESSEUSEMENT du coffre — ni le ZIP ni la matière source ne sont
/// jamais entièrement chargés en mémoire (exports volumineux, anti-OOM).
/// </summary>
public static class ArchiveEndpointMapping
{
    /// <summary>Permission de consultation (preuves d'audit). Référencée en chaîne : un module ne référence pas le Host.</summary>
    private const string ReadPermission = "liakont.read";

    /// <summary>Permission de paramétrage (réversibilité = dossier complet du tenant, opération sensible).</summary>
    private const string SettingsPermission = "liakont.settings";

    public static IEndpointRouteBuilder MapArchiveEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/v1/documents/{id}/audit-export — dossier d'export contrôle fiscal d'UN document (zip streamé).
        app.MapGet("/documents/{id:guid}/audit-export", async (
            Guid id,
            IFiscalControlExportService exportService,
            HttpContext context) =>
        {
            await WriteZipAsync(context, exportService.StreamForDocumentAsync(id, context.RequestAborted), $"audit-document-{id}.zip");
        }).RequireAuthorization(ReadPermission);

        // GET /api/v1/audit-export?from=&to= — dossier d'export PAR PÉRIODE (zip streamé). Au moins une
        // borne est exigée : l'export NON BORNÉ (coffre entier) relève de la réversibilité du tenant
        // (/tenant-export, permission liakont.settings), pas d'une simple lecture.
        app.MapGet("/audit-export", async (
            DateOnly? from,
            DateOnly? to,
            IFiscalControlExportService exportService,
            HttpContext context) =>
        {
            if (from is null && to is null)
            {
                return Results.BadRequest(
                    "Préciser au moins une borne (« from » et/ou « to »). L'export du coffre entier relève de la réversibilité du tenant (/api/v1/tenant-export, permission liakont.settings).");
            }

            // Toute validation d'entrée déterministe doit précéder l'ouverture du ZIP sur la réponse : une
            // fois le flux démarré (200 application/zip), on ne peut plus répondre 400. Les bornes inversées
            // sont donc rejetées ICI, avant WriteZipAsync (le service relève la même garde en profondeur).
            if (from is { } fromBound && to is { } toBound && toBound < fromBound)
            {
                return Results.BadRequest("La borne « to » précède la borne « from ».");
            }

            await WriteZipAsync(context, exportService.StreamForRangeAsync(from, to, context.RequestAborted), "audit-periode.zip");
            return Results.Empty;
        }).RequireAuthorization(ReadPermission);

        // GET /api/v1/tenant-export — RÉVERSIBILITÉ : dossier complet du tenant (zip streamé). Permission settings.
        app.MapGet("/tenant-export", async (
            ITenantReversibilityExportService reversibilityService,
            HttpContext context) =>
        {
            await WriteZipAsync(context, reversibilityService.StreamAsync(context.RequestAborted), "reversibilite-tenant.zip");
        }).RequireAuthorization(SettingsPermission);

        // POST /api/v1/archive/verify — vérification d'intégrité À LA DEMANDE de tout le coffre du tenant.
        // Retourne le rapport (200) ; l'altération est portée par le rapport (IsFullyVerified=false + résumé FR),
        // pas par un code d'erreur HTTP — la vérification a bien eu lieu, c'est son RÉSULTAT qui est non-OK.
        app.MapPost("/archive/verify", async (
            IArchiveVerifier verifier,
            HttpContext context) =>
        {
            ArchiveVerificationReport report = await verifier.VerifyTenantVaultAsync(context.RequestAborted);
            return Results.Ok(report);
        }).RequireAuthorization(ReadPermission);

        return app;
    }

    /// <summary>
    /// Écrit un flux PARESSEUX de fichiers en archive ZIP directement dans le corps de la réponse, entrée
    /// par entrée (ni le ZIP ni la matière source ne sont matérialisés en entier en mémoire : chaque pièce
    /// est lue du coffre puis écrite puis libérée). <see cref="ZipArchive"/> effectue des écritures
    /// SYNCHRONES (notamment du répertoire central à la fermeture) : on autorise donc l'IO synchrone sur
    /// CETTE réponse uniquement, le temps de produire l'archive.
    /// </summary>
    private static async Task WriteZipAsync(HttpContext context, IAsyncEnumerable<FiscalExportFile> files, string downloadName)
    {
        IHttpBodyControlFeature? bodyControl = context.Features.Get<IHttpBodyControlFeature>();
        if (bodyControl is not null)
        {
            bodyControl.AllowSynchronousIO = true;
        }

        context.Response.ContentType = "application/zip";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{downloadName}\"";

        using var zip = new ZipArchive(context.Response.Body, ZipArchiveMode.Create, leaveOpen: true);
        await foreach (FiscalExportFile file in files.WithCancellation(context.RequestAborted))
        {
            ZipArchiveEntry entry = zip.CreateEntry(file.Path, CompressionLevel.Optimal);
            await using Stream entryStream = entry.Open();
            await entryStream.WriteAsync(file.Content, context.RequestAborted);
        }
    }
}
