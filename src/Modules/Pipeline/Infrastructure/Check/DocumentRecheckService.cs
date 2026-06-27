namespace Liakont.Modules.Pipeline.Infrastructure.Check;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Domain.Ventilation;
using Liakont.Modules.Pipeline.Infrastructure.Serialization;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Re-vérification À LA DEMANDE d'UN document bloqué OU rejeté par la Plateforme Agréée (item API02b). Réutilise
/// la SOURCE UNIQUE de la décision de blocage fiscal (<see cref="DocumentCheckEvaluator"/>) — exactement comme le
/// CHECK (PIP01b) et la réconciliation des avoirs (PIP02) — pour ne jamais diverger sur ce qui bloque
/// (CLAUDE.md n°2/3). Exécutée DANS le scope de la requête HTTP : le tenant est déjà résolu
/// (<see cref="ITenantContext"/>), les services (staging, mapping, validation, cycle de vie, paramétrage) sont
/// routés vers la base du tenant (database-per-tenant). N'incorpore que des décisions OPÉRATEUR sourcées (verdict
/// B2C, F08 §A.4) ; la garde-fou production et toutes les autres règles restent appliquées sans changement.
/// </summary>
/// <remarks>
/// <para>Selon l'état d'ENTRÉE et la réévaluation, les transitions d'ÉTAT possibles sont : <c>Blocked → ReadyToSend</c>
/// (débloqué, cause corrigée), <c>RejectedByPa → ReadyToSend</c> (le rejet est réparable, on repart en envoi) et
/// <c>RejectedByPa → Blocked</c> (la cause du rejet n'est pas corrigée : le document quitte le cul-de-sac pour
/// montrer le motif à corriger — « bloquer plutôt qu'envoyer faux », CLAUDE.md n°3). Un document déjà <c>Blocked</c>
/// qui reste « pas prêt » NE transitionne PAS (la machine à états interdit <c>Blocked → Blocked</c>, TRK02). Mais
/// CHAQUE re-vérification qui tourne laisse une trace d'audit append-only attribuée à l'opérateur (item FIX02) :
/// au passage <c>ReadyToSend</c>, l'événement porte l'opérateur ; si le document reste bloqué, un événement
/// (<c>RecheckedStillBlocked</c> sans transition pour un Blocked, ou <c>DocumentBlocked</c> pour la transition
/// d'un RejectedByPa) inscrit le geste et le motif RÉÉVALUÉ — ce motif devient le motif COURANT affiché (plus de
/// motif périmé après rechargement). Les nouveaux motifs sont aussi renvoyés dans le résultat pour affichage
/// immédiat dans la console (WEB03b).</para>
/// <para>Au passage <c>ReadyToSend</c>, le snapshot de ventilation TVA sourcée est écrit AVANT la transition
/// (ADR-0015 §4, happened-before — sinon le document débloqué par recheck serait absent de l'agrégation de
/// paiement PIP03a), exactement comme le consommateur CHECK. Idempotent sur (document_id, mapping_version).</para>
/// </remarks>
internal sealed class DocumentRecheckService : IDocumentRecheckService
{
    /// <summary>Nom sérialisé de l'état <c>DocumentState.Blocked</c> (exposé en chaîne par les Contracts Documents).</summary>
    private const string BlockedStateName = "Blocked";

    /// <summary>Nom sérialisé de l'état <c>DocumentState.RejectedByPa</c> (exposé en chaîne par les Contracts Documents).</summary>
    private const string RejectedByPaStateName = "RejectedByPa";

    private readonly IServiceProvider _services;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    /// <summary>Construit le service de re-vérification (horloge système).</summary>
    public DocumentRecheckService(IServiceProvider services, ITenantContext tenantContext)
        : this(services, tenantContext, TimeProvider.System)
    {
    }

    /// <summary>Construit le service avec une horloge explicite (tests : déterminisme).</summary>
    internal DocumentRecheckService(IServiceProvider services, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _services = services;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<DocumentRecheckResult> RecheckAsync(Guid documentId, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operatorIdentity))
        {
            throw new ArgumentException("L'identité de l'opérateur est obligatoire pour une re-vérification (piste d'audit, F06 §3).", nameof(operatorIdentity));
        }

        var tenantId = _tenantContext.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException(
                "La re-vérification d'un document requiert un tenant résolu (contexte de requête).");
        }

        var queries = _services.GetRequiredService<IDocumentQueries>();
        var document = await queries.GetByIdAsync(documentId, cancellationToken);
        if (document is null)
        {
            return DocumentRecheckResult.NotFound();
        }

        // La re-vérification s'applique à un document BLOQUÉ ou REJETÉ par la PA (cohérent avec la pré-vérification
        // de l'endpoint). On mémorise l'état d'ENTRÉE pour router la branche « pas prêt » : un Blocked reste Blocked
        // (pas de transition), un RejectedByPa rejeté quitte le cul-de-sac pour Blocked avec le motif réévalué.
        var wasRejectedByPa = string.Equals(document.State, RejectedByPaStateName, StringComparison.Ordinal);
        if (!string.Equals(document.State, BlockedStateName, StringComparison.Ordinal) && !wasRejectedByPa)
        {
            return DocumentRecheckResult.NotBlocked(document.State);
        }

        var tenantSettings = _services.GetRequiredService<ITenantSettingsQueries>();
        var companyId = await tenantSettings.GetCurrentCompanyId(cancellationToken);
        if (companyId is null)
        {
            throw new InvalidOperationException(
                $"La re-vérification requiert un profil tenant (companyId) pour « {tenantId} » — paramétrage tenant requis (CFG02).");
        }

        // Relecture du contenu pivot stagé (PIP00). Le magasin re-vérifie le hash. Absent = transitoire (ADR-0014) ;
        // altéré = intégrité : dans les deux cas on ne peut pas re-vérifier — AUCUNE transition n'est appliquée (le
        // document garde son état d'entrée, Blocked ou RejectedByPa) et l'opérateur reçoit un message « contenu
        // indisponible » (CLAUDE.md n°12). L'état d'entrée est reporté tel quel (pas de fausse mention « Blocked »).
        var staging = _services.GetRequiredService<IPayloadStagingStore>();
        var key = new StagedPayloadKey(tenantId, documentId, document.PayloadHash);
        PivotDocumentDto pivot;
        try
        {
            var canonicalJson = await staging.ReadAsync(key, cancellationToken);
            pivot = PivotCanonicalJsonReader.Read(canonicalJson);
        }
        catch (StagedPayloadNotFoundException)
        {
            return DocumentRecheckResult.ContentUnavailable(document.State);
        }
        catch (StagedPayloadIntegrityException)
        {
            return DocumentRecheckResult.ContentUnavailable(document.State);
        }

        // RE-ÉVALUATION COMPLÈTE (mapping → garde-fou production → validation) via la source UNIQUE de la décision
        // fiscale, en incorporant le verdict B2C opérateur éventuellement posé sur le document (F08 §A.4).
        var decision = await DocumentCheckEvaluator.EvaluateAsync(
            _services,
            companyId.Value,
            documentId,
            document.DocumentNumber,
            pivot,
            buyerConfirmedB2C: document.BuyerConfirmedAsIndividual,
            cancellationToken: cancellationToken);

        var lifecycle = _services.GetRequiredService<IDocumentLifecycle>();

        if (!decision.IsReady)
        {
            // Pas prêt : on TRACE le geste opérateur et le motif réévalué (item FIX02) — la re-vérification n'est
            // jamais invisible dans la piste (F06 §3) et le motif affiché devient le dernier évalué (plus de motif
            // périmé après rechargement). Le cycle de vie vérifie l'état SOUS le verrou : un geste concurrent qui a
            // résolu/déplacé le document est rendu gracieusement (jamais un faux audit ni un 500), une vraie erreur
            // de persistance remonte.
            //   • Document REJETÉ par la PA → on le TRANSITIONNE RejectedByPa → Blocked : il quitte le cul-de-sac
            //     pour montrer la cause à corriger (« bloquer plutôt qu'envoyer faux », CLAUDE.md n°3).
            //   • Document déjà BLOQUÉ → aucune transition (Blocked → Blocked interdit), juste la trace du geste.
            var persisted = wasRejectedByPa
                ? await lifecycle.MarkBlockedByRecheckAsync(documentId, decision.BlockReason!, operatorIdentity, operatorName, cancellationToken)
                : await lifecycle.RecordRecheckStillBlockedAsync(documentId, decision.BlockReason!, operatorIdentity, operatorName, cancellationToken);
            return persisted == DocumentRecheckPersistOutcome.Persisted
                ? DocumentRecheckResult.StillBlocked(decision.BlockReason!)
                : await CurrentStateResultAsync(queries, documentId, cancellationToken);
        }

        // Prêt : snapshot de ventilation sourcée AVANT la transition (ADR-0015), puis Blocked → ReadyToSend
        // (événement d'audit attribué à l'opérateur qui a déclenché la re-vérification — item FIX02). Un changement
        // d'état concurrent est rendu gracieusement (le snapshot de ventilation, idempotent, reste sans effet).
        await WriteVentilationSnapshotAsync(document, decision, cancellationToken);
        var unblocked = await lifecycle.MarkReadyToSendByRecheckAsync(documentId, decision.MappingVersion!, operatorIdentity, operatorName, cancellationToken);
        return unblocked == DocumentRecheckPersistOutcome.Persisted
            ? DocumentRecheckResult.ReadyToSend()
            : await CurrentStateResultAsync(queries, documentId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DocumentBulkRecheckSummary> RecheckManyAsync(
        IReadOnlyList<Guid> documentIds, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);

        // Boucle la re-vérification UNITAIRE sur les identifiants DISTINCTS (un même document n'est re-vérifié et
        // audité qu'une fois) et agrège l'issue. Aucune règle fiscale ici — la décision de blocage reste la source
        // unique appelée par RecheckAsync, et CHAQUE document re-vérifié laisse sa trace d'audit append-only (FIX02).
        int total = 0, unblocked = 0, stillBlocked = 0, unavailable = 0, skipped = 0;
        foreach (var documentId in documentIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            total++;
            var result = await RecheckAsync(documentId, operatorIdentity, operatorName, cancellationToken);
            switch (result.Outcome)
            {
                case DocumentRecheckOutcome.ReadyToSend:
                    unblocked++;
                    break;
                case DocumentRecheckOutcome.StillBlocked:
                    stillBlocked++;
                    break;
                case DocumentRecheckOutcome.ContentUnavailable:
                    unavailable++;
                    break;
                default:
                    // NotFound / NotBlocked : l'état a déjà changé sous un geste concurrent — ignoré gracieusement
                    // (aucun fait d'audit n'est inscrit sur un document qui n'est plus dans l'état attendu).
                    skipped++;
                    break;
            }
        }

        return new DocumentBulkRecheckSummary
        {
            Total = total,
            Unblocked = unblocked,
            StillBlocked = stillBlocked,
            Unavailable = unavailable,
            Skipped = skipped,
        };
    }

    /// <summary>
    /// Relit l'état COURANT du document quand le cycle de vie verrouillé a refusé l'écriture (l'état a changé sous
    /// une action opérateur concurrente entre la lecture non verrouillée et l'écriture FOR UPDATE) et le mappe vers
    /// un résultat GRACIEUX — <see cref="DocumentRecheckResult.NotBlocked"/> (→ 409 « état modifié ») ou
    /// <see cref="DocumentRecheckResult.NotFound"/> — au lieu de laisser remonter une exception (HTTP 500). Garde la
    /// piste d'audit fidèle : aucun fait d'audit n'est inscrit sur un document qui n'est plus dans l'état attendu.
    /// </summary>
    private static async Task<DocumentRecheckResult> CurrentStateResultAsync(
        IDocumentQueries queries, Guid documentId, CancellationToken cancellationToken)
    {
        var current = await queries.GetByIdAsync(documentId, cancellationToken);
        return current is null
            ? DocumentRecheckResult.NotFound()
            : DocumentRecheckResult.NotBlocked(current.State);
    }

    /// <summary>
    /// Capture le snapshot de la ventilation TVA sourcée (ADR-0015) — même persistance idempotente que le
    /// consommateur CHECK — afin qu'un document débloqué par re-vérification soit présent dans l'agrégation de
    /// paiement (PIP03a) après la purge du staging.
    /// </summary>
    private async Task WriteVentilationSnapshotAsync(DocumentDto document, CheckDecision decision, CancellationToken cancellationToken)
    {
        var snapshot = new VentilationSnapshot
        {
            DocumentId = document.Id,
            DocumentNumber = document.DocumentNumber,
            SourceReference = document.SourceReference,
            OperationCategory = decision.OperationCategory!.Value,
            MappingVersion = decision.MappingVersion!,
            Lines = decision.Ventilation!,
            CreatedUtc = _timeProvider.GetUtcNow(),
        };

        await _services.GetRequiredService<IVentilationSnapshotStore>().SaveAsync(snapshot, cancellationToken);
    }
}
