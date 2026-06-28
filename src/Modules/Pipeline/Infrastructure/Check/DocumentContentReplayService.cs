namespace Liakont.Modules.Pipeline.Infrastructure.Check;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Infrastructure.Serialization;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TvaMapping.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Rejeu read-time du contenu d'un document pour l'affichage du détail ligne à ligne (BUG-5). Relit le pivot
/// SOURCE stagé (PIP00) et REJOUE le mapping TVA avec la SOURCE UNIQUE de la classification fiscale
/// (<see cref="CheckTvaMapping"/> + table validée du tenant via <see cref="ITvaMappingService"/>) — exactement le
/// moteur du CHECK (<see cref="DocumentCheckEvaluator"/>) et de l'envoi
/// (<c>SendTenantJob.ReadStagedPivotAsync</c>). Aucune seconde implémentation divergente (CLAUDE.md n°2), aucun
/// montant recalculé, aucune valeur fiscale inventée. Exécutée DANS le scope de la requête (le tenant est résolu —
/// <see cref="ITenantContext"/>, database-per-tenant) ; LECTURE PURE (aucune transition, aucune écriture).
/// </summary>
/// <remarks>
/// <para>Quand le mapping PASSE, on expose le pivot ENRICHI (catégorie/VATEX/taux par ligne). Quand il BLOQUE (ex.
/// régime non couvert, ligne hors forme), on expose le pivot SOURCE tel que lu — régime source présent,
/// catégorie/VATEX VIDES : c'est le diagnostic FACTUEL d'un document Bloqué (l'opérateur voit ce qui a été lu et
/// que la classification n'a pas abouti), jamais une catégorie devinée (CLAUDE.md n°2). C'est légitime pour un
/// document Bloqué ; un document Prêt-à-envoyer a, lui, un mapping qui réussit.</para>
/// <para>L'émetteur (SIREN/raison sociale) n'est PAS résolu ici : il n'entre pas dans le contenu LIGNE à LIGNE
/// (lignes + catégorie/VATEX/taux + cohérence totaux↔lignes) qu'expose ce service. <see cref="CheckTvaMapping"/>
/// opère sur les seules lignes (codes régime + ventilations), donc le contenu affiché ne dépend pas de l'identité
/// émetteur — on évite une lecture de profil tenant inutile. En revanche, les MENTIONS DE FACTURATION effectives
/// (BUG-26, BT-20 + notes légales FR BR-FR-05) SONT résolues, via la SOURCE UNIQUE d'injection
/// (<see cref="PivotEmitterEnricher.Enrich"/> avec <c>profile</c>/<c>fiscal</c> nuls : seules les mentions sont
/// remplies, l'émetteur reste inchangé) : on affiche ce que le document PORTE, sinon le défaut tenant
/// (<see cref="ITenantSettingsQueries.GetBillingMentions"/>) — jamais une mention inventée (CLAUDE.md n°2). Société
/// du tenant inconnue (paramétrage incomplet) → aucune mention (le contenu ligne reste exposé).</para>
/// <para>Si le pivot source stagé n'est plus disponible (purgé après émission — ADR-0014 §4 —, absent, ou
/// intégrité KO), on retourne <see cref="DocumentContentReplay.Unavailable"/> : l'appelant retombe sur le
/// snapshot transmis (comportement historique préservé pour un document déjà émis/rejeté).</para>
/// </remarks>
internal sealed class DocumentContentReplayService : IDocumentContentReplayService
{
    private readonly IServiceProvider _services;
    private readonly ITenantContext _tenantContext;

    /// <summary>Construit le service de rejeu de contenu (scopé requête).</summary>
    public DocumentContentReplayService(IServiceProvider services, ITenantContext tenantContext)
    {
        _services = services;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<DocumentContentReplay> ReplayAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException(
                "Le rejeu du contenu d'un document requiert un tenant résolu (contexte de requête).");
        }

        var documents = _services.GetRequiredService<IDocumentQueries>();
        var document = await documents.GetByIdAsync(documentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            // Document inconnu du tenant : aucun contenu source à relire (l'appelant rendra « introuvable »).
            return DocumentContentReplay.Unavailable;
        }

        // Relecture du pivot SOURCE stagé (PIP00). Le magasin re-vérifie le hash. Absent (transitoire / purgé après
        // émission, ADR-0014) ou altéré (intégrité) : on NE peut PAS rejouer → contenu indisponible, l'appelant
        // retombe sur le snapshot transmis (un document émis a déjà son pivot dans le coffre WORM).
        var staging = _services.GetRequiredService<IPayloadStagingStore>();
        var key = new StagedPayloadKey(tenantId, documentId, document.PayloadHash);
        PivotDocumentDto sourcePivot;
        try
        {
            var canonicalJson = await staging.ReadAsync(key, cancellationToken).ConfigureAwait(false);
            sourcePivot = PivotCanonicalJsonReader.Read(canonicalJson);
        }
        catch (StagedPayloadNotFoundException)
        {
            return DocumentContentReplay.Unavailable;
        }
        catch (StagedPayloadIntegrityException)
        {
            return DocumentContentReplay.Unavailable;
        }

        // Compagnie du tenant (clé d'isolation du mapping ET des mentions de facturation). Absente = paramétrage
        // tenant incomplet : on expose tout de même le pivot SOURCE (lignes + régime lu, catégorie/VATEX vides) —
        // le détail reste diagnostiquable — SANS mentions (rien à résoudre sans société : jamais inventées).
        var tenantSettings = _services.GetRequiredService<ITenantSettingsQueries>();
        var companyId = await tenantSettings.GetCurrentCompanyId(cancellationToken).ConfigureAwait(false);
        if (companyId is null)
        {
            return DocumentContentReplay.From(sourcePivot);
        }

        // Mentions de facturation EFFECTIVES (BUG-26, F12-A §3.4) : défaut TENANT, injecté SUR le pivot d'affichage
        // au read-time UNIQUEMENT s'il ne les porte pas (la valeur du document prime). Résolues UNE fois ; appliquées
        // au pivot finalement exposé (source OU enrichi-mapping) via la SOURCE UNIQUE d'injection (l'enricher).
        var mentions = await tenantSettings.GetBillingMentions(companyId.Value, cancellationToken).ConfigureAwait(false);

        // REJEU du mapping via la SOURCE UNIQUE (CheckTvaMapping) — même chemin qu'au CHECK et qu'à l'envoi.
        // Aucune ligne de forme mappable (toutes hors forme V1) ou table absente/mapping bloqué → on expose le
        // pivot SOURCE (régime lu, catégorie/VATEX vides = le FAIT du blocage, jamais deviné — CLAUDE.md n°2).
        var plan = CheckTvaMapping.BuildPlan(sourcePivot);
        if (plan.Requests.Count == 0)
        {
            return ReplayWithMentions(sourcePivot, mentions);
        }

        var mapping = await _services.GetRequiredService<ITvaMappingService>()
            .MapAsync(companyId.Value, plan.Requests, cancellationToken).ConfigureAwait(false);
        if (!mapping.TableExists)
        {
            return ReplayWithMentions(sourcePivot, mentions);
        }

        var evaluation = CheckTvaMapping.Evaluate(sourcePivot, plan, mapping);
        return evaluation.IsBlocked
            ? ReplayWithMentions(sourcePivot, mentions)
            : ReplayWithMentions(evaluation.EnrichedDocument!, mentions);
    }

    /// <summary>
    /// Surface le pivot d'affichage (source ou enrichi-mapping) AVEC ses mentions de facturation EFFECTIVES :
    /// l'enricher injecte le défaut TENANT (BT-20 + notes BR-FR-05) seulement quand le document ne les porte pas
    /// (la valeur du document prime — F12-A §3.4). <c>profile</c>/<c>fiscal</c> nuls : l'émetteur et la nature
    /// d'opération restent INCHANGÉS (le contenu ligne ne dépend pas de l'identité émetteur). Mentions tenant
    /// absentes → l'enricher renvoie le pivot tel quel (mentions vides) — jamais inventées (CLAUDE.md n°2).
    /// </summary>
    private static DocumentContentReplay ReplayWithMentions(PivotDocumentDto displayPivot, BillingMentionsDto? mentions) =>
        DocumentContentReplay.From(PivotEmitterEnricher.Enrich(displayPivot, profile: null, fiscal: null, mentions));
}
