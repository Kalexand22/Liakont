namespace Liakont.Modules.Ged.Contracts.Commands;

using System.Collections.Generic;
using Liakont.Agent.Contracts.Ged;
using Liakont.Modules.Ged.Contracts.DTOs;
using MediatR;

/// <summary>
/// Ingère un LOT de documents gérés (canal GED, F19 §2.4/§4.3, item GED05b). Pour CHAQUE document : sérialisation
/// canonique GED + empreinte, décision d'anti-doublon (Duplicate/AcceptedAltered/AcceptedNew), staging du pivot
/// AVANT la transaction (ADR-0014), puis — si accepté — INSERT du registre <c>ged_ingestion.ged_received_documents</c>
/// + écriture de <c>ManagedDocumentReceivedV1</c> dans l'outbox, ATOMIQUEMENT en base système (RL-03). AUCUN Document
/// fiscal n'est créé (le canal GED n'appelle jamais <c>IDocumentIntake</c>). Tenant-scopé par la clé API de l'agent.
/// </summary>
public sealed record IngestManagedDocumentBatchCommand : IRequest<ManagedDocumentBatchResultDto>
{
    /// <summary>Tenant propriétaire (slug résolu depuis la clé API de l'agent authentifié).</summary>
    public required string TenantId { get; init; }

    /// <summary>Les documents gérés ingérés BRUTS du lot (chacun produit un résultat individuel).</summary>
    public required IReadOnlyList<IngestedDocumentDto> Documents { get; init; }
}
