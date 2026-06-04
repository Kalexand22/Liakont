namespace Liakont.Agent.Contracts.Transport;

using System;
using System.Collections.Generic;

/// <summary>
/// Réponse à un lot poussé par l'agent (F12 §3.4) : un résultat par document, dans l'ordre des
/// documents de la requête.
/// </summary>
public sealed class PushBatchResponseDto
{
    /// <summary>Crée une réponse de lot.</summary>
    /// <param name="results">Résultats individuels, un par document.</param>
    public PushBatchResponseDto(IReadOnlyList<DocumentPushResultDto>? results = null)
    {
        Results = results ?? Array.Empty<DocumentPushResultDto>();
    }

    /// <summary>Résultats individuels, un par document.</summary>
    public IReadOnlyList<DocumentPushResultDto> Results { get; }
}
