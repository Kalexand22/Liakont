namespace Liakont.Modules.Reconciliation.Contracts.DTOs;

using System;

/// <summary>
/// Proposition de rapprochement de CONFIANCE MOYENNE en attente de confirmation opérateur (item TRK07).
/// Consommée par l'API et la console (API04/WEB08). La confiance et la stratégie sont exposées
/// en TEXTE (découplage de l'énumération de domaine).
/// </summary>
public sealed record ReconciliationProposalDto(
    Guid QueueEntryId,
    string PoolPdfId,
    string FileName,
    Guid ProposedDocumentId,
    string Strategy,
    string Confidence,
    string Detail,
    DateTimeOffset CreatedUtc);
