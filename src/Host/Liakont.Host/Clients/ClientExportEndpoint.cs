namespace Liakont.Host.Clients;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Liakont.Host.Security;
using Liakont.Modules.Archive.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Endpoint opérateur d'export de RÉVERSIBILITÉ d'UN client choisi sur l'écran Clients (OPS06a ; F12 §6.3).
/// Réutilise la capacité de réversibilité du module Archive (<see cref="ITenantReversibilityExportService"/>,
/// le MÊME bundle que <c>/api/v1/tenant-export</c>) mais pour le tenant désigné dans l'URL — l'opérateur
/// d'instance n'a pas de tenant courant (super-admin hors périmètre tenant, ADR-0021), il choisit la cible.
/// <para>
/// Le scope du tenant CIBLE est établi SERVER-SIDE via <see cref="ITenantScopeFactory.Create"/> (le même
/// mécanisme que les actions de <c>ClientConsoleService</c> et les jobs tenant) : le service de réversibilité
/// est résolu DANS ce scope, donc toutes ses lectures passent par la connexion du tenant choisi — c'est une
/// lecture cross-tenant LÉGITIME de la surface Supervision/Clients (CLAUDE.md n°9), jamais une requête
/// cross-tenant ni une écriture. Garde <c>liakont.supervision</c> (la même que l'écran Clients).
/// </para>
/// <para>
/// Le téléchargement passe par cet endpoint HTTP (ZIP en pièce jointe) consommé par un lien
/// <c>&lt;a href … download&gt;</c> de la page : le principe « actions in-process, pas de HTTP en boucle
/// locale » de l'écran Clients vise les ACTIONS (commandes MediatR), pas les téléchargements de fichier —
/// même schéma que l'export self-service de <c>ParametrageView</c> (API03).
/// </para>
/// </summary>
public static class ClientExportEndpoint
{
    public static IEndpointRouteBuilder MapClientExportEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/v1/clients/{tenantId}/tenant-export — réversibilité du client choisi (zip streamé).
        app.MapGet("/clients/{tenantId}/tenant-export", async (
            string tenantId,
            ITenantQueries tenantQueries,
            ITenantScopeFactory scopeFactory,
            HttpContext context) =>
        {
            // Toute validation déterministe AVANT d'ouvrir le ZIP sur la réponse : une fois le flux démarré
            // (200 application/zip) on ne peut plus répondre 404. Le client cible doit exister au registre.
            TenantDto? tenant = await tenantQueries.GetByIdAsync(tenantId, context.RequestAborted);
            if (tenant is null)
            {
                return Results.NotFound($"Client « {tenantId} » introuvable sur cette instance.");
            }

            await using ITenantScope scope = scopeFactory.Create(tenantId);
            var reversibilityService = scope.Services.GetRequiredService<ITenantReversibilityExportService>();
            await WriteZipAsync(
                context,
                reversibilityService.StreamAsync(context.RequestAborted),
                $"reversibilite-{Sanitize(tenantId)}.zip");
            return Results.Empty;
        }).RequireAuthorization(LiakontPermissions.Supervision);

        return app;
    }

    /// <summary>
    /// Écrit un flux PARESSEUX de fichiers en archive ZIP directement dans le corps de la réponse, entrée par
    /// entrée (ni le ZIP ni la matière source ne sont matérialisés en entier — exports volumineux, anti-OOM).
    /// <see cref="ZipArchive"/> effectue des écritures SYNCHRONES (répertoire central à la fermeture) : on
    /// autorise donc l'IO synchrone sur CETTE réponse uniquement, le temps de produire l'archive. (Même schéma
    /// que <c>ArchiveEndpointMapping.WriteZipAsync</c> — copié plutôt qu'exposé pour garder l'export opérateur
    /// confiné au Host et ne pas élargir la surface publique du module Archive.)
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

    /// <summary>Réduit l'identifiant de tenant au jeu sûr pour un nom de fichier d'en-tête (anti-injection).</summary>
    private static string Sanitize(string tenantId)
    {
        var builder = new StringBuilder(tenantId.Length);
        foreach (char c in tenantId)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-');
        }

        string safe = builder.ToString();
        return string.IsNullOrEmpty(safe) ? "tenant" : safe;
    }
}
