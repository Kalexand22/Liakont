namespace Liakont.Modules.TvaMapping.Web;

using System;
using System.Collections.Generic;
using System.Threading;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Liakont.Modules.TvaMapping.Contracts.Queries;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Endpoints de PARAMÉTRAGE COMPTABLE de la table de mapping TVA pour la console (API04), montés sous
/// <c>/api/v1/settings/tva-mapping</c> par le Host. Toute MUTATION passe par le moteur d'édition TVA05
/// (commandes MediatR) — invalidation automatique de la validation + journal append-only + identité de
/// l'opérateur, RIEN n'est dupliqué ici. Aucune logique métier/fiscale dans l'endpoint : la catégorie,
/// le VATEX, le taux et la validation structurelle sont du ressort des handlers (CLAUDE.md n°2/3/19).
/// <para>
/// Permissions : la LECTURE exige <c>liakont.read</c> (consultation) ; toute ÉDITION exige
/// <c>liakont.settings</c> (paramétrage fiscal — un utilisateur « actions » ne peut PAS éditer la table,
/// docs/architecture/identity-permissions-liakont.md). La société (tenant) est résolue par le contexte
/// (<see cref="ICompanyFilter"/>) — jamais passée par l'appelant (tenant-scoping, CLAUDE.md n°9).
/// </para>
/// </summary>
public static class TvaMappingEndpointMapping
{
    /// <summary>Permission de consultation (chaîne : un module ne référence pas le Host — frontière de dépendance).</summary>
    private const string ReadPermission = "liakont.read";

    /// <summary>Permission de paramétrage fiscal du tenant (édition de la table TVA).</summary>
    private const string SettingsPermission = "liakont.settings";

    public static IEndpointRouteBuilder MapTvaMappingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/settings/tva-mapping");

        // GET /api/v1/settings/tva-mapping — table du tenant (+ état de validation) + journal des modifications.
        group.MapGet(string.Empty, async (
            IActorContextAccessor actorContext,
            ITvaMappingQueries queries,
            CancellationToken ct) =>
        {
            // La société est résolue par le contexte (même source que les commandes d'édition, qui passent
            // par ICompanyFilter → IActorContext.CompanyId) — lecture et écriture portent ainsi sur la même
            // table (CLAUDE.md n°9). Jamais passée par l'appelant.
            var companyId = ResolveCompanyId(actorContext.Current);
            var table = await queries.GetMappingTable(companyId, ct);
            var changeLog = await queries.GetChangeLog(companyId, ct);
            return Results.Ok(new TvaMappingViewResponse { Table = table, ChangeLog = changeLog });
        }).RequireAuthorization(ReadPermission);

        // POST /api/v1/settings/tva-mapping/rules — ajoute une règle (moteur TVA05 ; invalide la validation).
        group.MapPost("/rules", async (AddMappingRuleCommand command, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(command, ct);
            return Results.NoContent();
        }).RequireAuthorization(SettingsPermission);

        // PUT /api/v1/settings/tva-mapping/rules/{sourceRegimeCode}/{part} — modifie une règle (clé = couple).
        group.MapPut("/rules/{sourceRegimeCode}/{part}", async (
            string sourceRegimeCode,
            string part,
            UpdateMappingRuleCommand command,
            ISender sender,
            CancellationToken ct) =>
        {
            // La clé (code régime + part) de l'URL doit correspondre au corps : on n'édite que la règle désignée.
            if (!string.Equals(sourceRegimeCode, command.SourceRegimeCode, StringComparison.Ordinal)
                || !string.Equals(part, command.Part, StringComparison.Ordinal))
            {
                return Results.BadRequest(
                    "La règle de l'URL (code régime / part) ne correspond pas au corps de la requête.");
            }

            await sender.Send(command, ct);
            return Results.NoContent();
        }).RequireAuthorization(SettingsPermission);

        // DELETE /api/v1/settings/tva-mapping/rules/{sourceRegimeCode}/{part} — supprime une règle.
        group.MapDelete("/rules/{sourceRegimeCode}/{part}", async (
            string sourceRegimeCode,
            string part,
            ISender sender,
            CancellationToken ct) =>
        {
            await sender.Send(
                new RemoveMappingRuleCommand { SourceRegimeCode = sourceRegimeCode, Part = part }, ct);
            return Results.NoContent();
        }).RequireAuthorization(SettingsPermission);

        // POST /api/v1/settings/tva-mapping/validate — marque la table validée (validatedBy = opérateur courant).
        group.MapPost("/validate", async (
            IActorContextAccessor actorContext,
            ISender sender,
            CancellationToken ct) =>
        {
            // validatedBy provient de l'identité AUTHENTIFIÉE (jamais du corps : un opérateur ne peut pas
            // signer la validation au nom d'un autre — CLAUDE.md n°12, identité de l'opérateur journalisée).
            var validatedBy = ResolveOperatorIdentity(actorContext.Current);
            await sender.Send(new ValidateMappingTableCommand { ValidatedBy = validatedBy }, ct);
            return Results.NoContent();
        }).RequireAuthorization(SettingsPermission);

        return app;
    }

    /// <summary>
    /// Société (tenant) du contexte courant. Même résolution que <c>ICompanyFilter.GetRequiredCompanyId</c>
    /// (la lecture et les commandes d'édition portent donc sur la même table). Lève si aucune société n'est
    /// résolue plutôt que de servir une table arbitraire.
    /// </summary>
    private static Guid ResolveCompanyId(IActorContext actor)
    {
        return actor.CompanyId
            ?? throw new InvalidOperationException(
                "Aucune société résolue dans le contexte courant : impossible de lire la table TVA du tenant (CLAUDE.md n°9).");
    }

    /// <summary>
    /// Identité lisible de l'opérateur courant pour <c>validatedBy</c> : nom affiché, à défaut e-mail, à
    /// défaut identifiant. Lève si aucune identité n'est résolue (ne devrait pas arriver sur un endpoint
    /// authentifié) plutôt que d'enregistrer une validation anonyme.
    /// </summary>
    private static string ResolveOperatorIdentity(IActorContext actor)
    {
        if (!string.IsNullOrWhiteSpace(actor.DisplayName))
        {
            return actor.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(actor.Email))
        {
            return actor.Email;
        }

        if (actor.UserId != Guid.Empty)
        {
            return actor.UserId.ToString();
        }

        throw new InvalidOperationException(
            "Identité de l'opérateur introuvable : impossible de valider la table TVA sans valideur (CLAUDE.md n°12).");
    }

    /// <summary>
    /// Réponse de la vue de paramétrage TVA (API04) : la table du tenant (ou <c>null</c> si non encore
    /// paramétrée) et son journal de modifications (append-only, lecture seule).
    /// </summary>
    public sealed record TvaMappingViewResponse
    {
        /// <summary>Table de mapping du tenant, ou <c>null</c> si aucune table n'est paramétrée (CFG02 non fait).</summary>
        public MappingTableDto? Table { get; init; }

        /// <summary>Journal des modifications de la table, du plus récent au plus ancien (vide si aucune).</summary>
        public required IReadOnlyList<MappingChangeLogEntryDto> ChangeLog { get; init; }
    }
}
