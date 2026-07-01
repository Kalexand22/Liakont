namespace Liakont.Modules.Ged.Application.Index;

using System;

/// <summary>Un document correspondant : métadonnées d'affichage (aucune valeur d'axe confidentielle).</summary>
public sealed record DocumentSearchHit
{
    /// <summary>Identité GED du document.</summary>
    public required Guid ManagedDocumentId { get; init; }

    /// <summary>Titre (référence source) du document.</summary>
    public required string Title { get; init; }

    /// <summary>Libellé métier libre (pas un état fiscal) ; peut être nul.</summary>
    public string? DocKind { get; init; }

    /// <summary>Statut d'indexation : <c>draft</c>|<c>indexed</c>|<c>archived</c>|<c>deferred</c>.</summary>
    public required string Status { get; init; }
}
