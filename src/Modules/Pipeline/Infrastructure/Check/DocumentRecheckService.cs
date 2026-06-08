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
/// <para>La seule transition possible est <c>Blocked → ReadyToSend</c> : la machine à états interdit
/// <c>Blocked → Blocked</c> (TRK02). Un document toujours bloqué après re-vérification ne subit AUCUNE
/// transition (pas de re-blocage, pas de churn de la piste d'audit append-only) ; ses nouveaux motifs sont
/// renvoyés dans le résultat pour affichage immédiat dans la console (WEB03b).</para>
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
    public async Task<DocumentRecheckResult> RecheckAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
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

        if (!decision.IsReady)
        {
            return DocumentRecheckResult.StillBlocked(decision.BlockReason!);
        }

        // Prêt : snapshot de ventilation sourcée AVANT la transition (ADR-0015), puis Blocked → ReadyToSend.
        await WriteVentilationSnapshotAsync(document, decision, cancellationToken);
        await _services.GetRequiredService<IDocumentLifecycle>()
            .MarkReadyToSendAsync(documentId, decision.MappingVersion!, cancellationToken);

        return DocumentRecheckResult.ReadyToSend();
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
