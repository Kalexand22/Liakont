namespace Liakont.Agent.Core.Transport;

using System;
using System.Collections.Generic;
using Liakont.Agent.Contracts.Transport;

/// <summary>
/// Résultat d'un push de lot de documents (POST /api/agent/v1/documents/batch). Porte la catégorie de
/// réponse et, quand elle vaut <see cref="PlatformResponseKind.Ok"/>, les résultats individuels par
/// document (dans l'ordre des documents envoyés — F12 §3.4).
/// </summary>
public sealed class PushBatchOutcome
{
    /// <summary>Crée un résultat de push de lot.</summary>
    /// <param name="kind">Catégorie de réponse de la plateforme.</param>
    /// <param name="results">Résultats individuels par document (seulement pour <see cref="PlatformResponseKind.Ok"/>).</param>
    /// <param name="reason">Détail (diagnostic / motif d'échec), si applicable.</param>
    public PushBatchOutcome(
        PlatformResponseKind kind,
        IReadOnlyList<DocumentPushResultDto>? results = null,
        string? reason = null)
    {
        Kind = kind;
        Results = results ?? Array.Empty<DocumentPushResultDto>();
        Reason = reason;
    }

    /// <summary>Catégorie de réponse de la plateforme.</summary>
    public PlatformResponseKind Kind { get; }

    /// <summary>Résultats individuels par document (vide hors réponse 200).</summary>
    public IReadOnlyList<DocumentPushResultDto> Results { get; }

    /// <summary>Détail (diagnostic / motif d'échec).</summary>
    public string? Reason { get; }
}
