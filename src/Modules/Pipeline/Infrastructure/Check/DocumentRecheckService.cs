namespace Liakont.Modules.Pipeline.Infrastructure.Check;

using System;
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
/// Re-vérification À LA DEMANDE d'UN document bloqué (item API02b). Réutilise la SOURCE UNIQUE de la décision
/// de blocage fiscal (<see cref="DocumentCheckEvaluator"/>) — exactement comme le CHECK (PIP01b) et la
/// réconciliation des avoirs (PIP02) — pour ne jamais diverger sur ce qui bloque (CLAUDE.md n°2/3). Exécutée
/// DANS le scope de la requête HTTP : le tenant est déjà résolu (<see cref="ITenantContext"/>), les services
/// (staging, mapping, validation, cycle de vie, paramétrage) sont routés vers la base du tenant
/// (database-per-tenant). N'incorpore que des décisions OPÉRATEUR sourcées (verdict B2C, F08 §A.4) ; la
/// garde-fou production et toutes les autres règles restent appliquées sans changement.
/// </summary>
/// <remarks>
/// <para>La seule transition d'ÉTAT possible est <c>Blocked → ReadyToSend</c> : la machine à états interdit
/// <c>Blocked → Blocked</c> (TRK02). Mais CHAQUE re-vérification qui tourne laisse une trace d'audit append-only
/// attribuée à l'opérateur (item FIX02) : au déblocage, l'événement <c>ReadyToSend</c> porte l'opérateur ; si le
/// document reste bloqué, un événement <c>RecheckedStillBlocked</c> (SANS transition d'état) inscrit le geste et
/// le motif RÉÉVALUÉ — ce motif devient le motif COURANT affiché (plus de motif périmé après rechargement). Les
/// nouveaux motifs sont aussi renvoyés dans le résultat pour affichage immédiat dans la console (WEB03b).</para>
/// <para>Au passage <c>ReadyToSend</c>, le snapshot de ventilation TVA sourcée est écrit AVANT la transition
/// (ADR-0015 §4, happened-before — sinon le document débloqué par recheck serait absent de l'agrégation de
/// paiement PIP03a), exactement comme le consommateur CHECK. Idempotent sur (document_id, mapping_version).</para>
/// </remarks>
internal sealed class DocumentRecheckService : IDocumentRecheckService
{
    /// <summary>Nom sérialisé de l'état <c>DocumentState.Blocked</c> (exposé en chaîne par les Contracts Documents).</summary>
    private const string BlockedStateName = "Blocked";

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
    public async Task<DocumentRecheckResult> RecheckAsync(Guid documentId, string operatorIdentity, CancellationToken cancellationToken = default)
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

        if (!string.Equals(document.State, BlockedStateName, StringComparison.Ordinal))
        {
            // La re-vérification ne s'applique qu'à un document bloqué (cohérent avec la pré-vérification de l'endpoint).
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
        // altéré = intégrité : dans les deux cas on ne peut pas re-vérifier — le document reste Blocked (déjà bloqué,
        // jamais Blocked → Blocked) et l'opérateur reçoit un message « contenu indisponible » (CLAUDE.md n°12).
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
            return DocumentRecheckResult.ContentUnavailable();
        }
        catch (StagedPayloadIntegrityException)
        {
            return DocumentRecheckResult.ContentUnavailable();
        }

        // RE-ÉVALUATION COMPLÈTE (mapping → garde-fou production → validation) via la source UNIQUE de la décision
        // fiscale, en incorporant le verdict B2C opérateur éventuellement posé sur le document (F08 §A.4).
        var decision = await DocumentCheckEvaluator.EvaluateAsync(
            _services,
            companyId.Value,
            document.DocumentNumber,
            pivot,
            buyerConfirmedB2C: document.BuyerConfirmedAsIndividual,
            cancellationToken: cancellationToken);

        var lifecycle = _services.GetRequiredService<IDocumentLifecycle>();

        if (!decision.IsReady)
        {
            // Toujours bloqué : aucune transition (Blocked → Blocked interdit), mais on TRACE le geste opérateur
            // et le motif réévalué (item FIX02) — la re-vérification n'est plus invisible dans la piste (F06 §3)
            // et le motif affiché devient le dernier évalué (plus de motif périmé après rechargement).
            try
            {
                await lifecycle.RecordRecheckStillBlockedAsync(documentId, decision.BlockReason!, operatorIdentity, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Course étroite : entre la lecture NON verrouillée (ci-dessus) et l'écriture VERROUILLÉE (FOR UPDATE)
                // du cycle de vie, un geste opérateur concurrent (résolution manuelle, ou un recheck qui débloque) a
                // transitionné le document hors de Blocked. On ne trace JAMAIS un faux « toujours bloqué » sur un
                // document qui ne l'est plus, et on ne propage pas un 500 : on rend l'état courant (→ 409 « la
                // re-vérification ne s'applique qu'à un document bloqué »). InvalidDocumentTransitionException dérive
                // d'InvalidOperationException ; on l'attrape par sa base SANS référencer le Domain Documents (frontière).
                return await CurrentStateResultAsync(queries, documentId, cancellationToken);
            }

            return DocumentRecheckResult.StillBlocked(decision.BlockReason!);
        }

        // Prêt : snapshot de ventilation sourcée AVANT la transition (ADR-0015), puis Blocked → ReadyToSend
        // (événement d'audit attribué à l'opérateur qui a déclenché la re-vérification — item FIX02).
        await WriteVentilationSnapshotAsync(document, decision, cancellationToken);
        try
        {
            await lifecycle.MarkReadyToSendByRecheckAsync(documentId, decision.MappingVersion!, operatorIdentity, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Même course concurrente, côté déblocage : la machine à états refuse Blocked → ReadyToSend si l'état a
            // changé entre la lecture non verrouillée et l'écriture verrouillée. On rend l'état courant plutôt qu'un
            // 500 (le snapshot de ventilation, idempotent sur (document_id, mapping_version), reste sans effet).
            return await CurrentStateResultAsync(queries, documentId, cancellationToken);
        }

        return DocumentRecheckResult.ReadyToSend();
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
