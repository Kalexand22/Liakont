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
/// <see cref="IArchiveVerifier"/>) et écrivent le résultat en archive ZIP directement dans le flux de
/// réponse (entrée par entrée, jamais bufferisé entièrement en mémoire).
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
            FiscalControlExport export = await exportService.BuildForDocumentAsync(id, context.RequestAborted);
            await WriteZipAsync(context, export.Files, $"audit-document-{id}.zip");
        }).RequireAuthorization(ReadPermission);

        // GET /api/v1/audit-export?from=&to= — dossier d'export PAR PÉRIODE (zip streamé).
        app.MapGet("/audit-export", async (
            DateOnly? from,
            DateOnly? to,
            IFiscalControlExportService exportService,
            HttpContext context) =>
        {
            FiscalControlExport export = await exportService.BuildForRangeAsync(from, to, context.RequestAborted);
            await WriteZipAsync(context, export.Files, "audit-periode.zip");
        }).RequireAuthorization(ReadPermission);

        // GET /api/v1/tenant-export — RÉVERSIBILITÉ : dossier complet du tenant (zip streamé). Permission settings.
        app.MapGet("/tenant-export", async (
            ITenantReversibilityExportService reversibilityService,
            HttpContext context) =>
        {
            TenantReversibilityExport export = await reversibilityService.BuildAsync(context.RequestAborted);
            await WriteZipAsync(context, export.Files, "reversibilite-tenant.zip");
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
    /// Écrit une liste de fichiers en archive ZIP directement dans le corps de la réponse, entrée par
    /// entrée (le ZIP n'est jamais matérialisé en entier en mémoire). <see cref="ZipArchive"/> effectue des
    /// écritures SYNCHRONES (notamment du répertoire central à la fermeture) : on autorise donc l'IO
    /// synchrone sur CETTE réponse uniquement, le temps de produire l'archive.
    /// </summary>
    private static async Task WriteZipAsync(HttpContext context, IReadOnlyList<FiscalExportFile> files, string downloadName)
    {
        IHttpBodyControlFeature? bodyControl = context.Features.Get<IHttpBodyControlFeature>();
        if (bodyControl is not null)
        {
            bodyControl.AllowSynchronousIO = true;
        }

        context.Response.ContentType = "application/zip";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{downloadName}\"";

        using var zip = new ZipArchive(context.Response.Body, ZipArchiveMode.Create, leaveOpen: true);
        foreach (FiscalExportFile file in files)
        {
            ZipArchiveEntry entry = zip.CreateEntry(file.Path, CompressionLevel.Optimal);
            await using Stream entryStream = entry.Open();
            await entryStream.WriteAsync(file.Content, context.RequestAborted);
        }
    }
}
