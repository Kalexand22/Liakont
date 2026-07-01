namespace Liakont.Modules.Ged.Contracts.DTOs;

using System.Collections.Generic;

/// <summary>
/// Réponse d'un lot d'ingestion de documents gérés (canal GED, F19 §2.4) : le verdict INDIVIDUEL de chaque document
/// (jamais un rejet global du lot pour un seul document invalide).
/// </summary>
/// <param name="Results">Les verdicts individuels, dans l'ordre des documents du lot.</param>
public sealed record ManagedDocumentBatchResultDto(IReadOnlyList<ManagedDocumentPushResultDto> Results);
