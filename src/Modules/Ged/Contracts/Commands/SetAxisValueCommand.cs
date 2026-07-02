namespace Liakont.Modules.Ged.Contracts.Commands;

using System;
using MediatR;

/// <summary>
/// Porte une valeur d'axe BRUTE sur un document géré (F19 §3.7). Le handler (GED04) résout l'axe par son code
/// (refus si inconnu/inactif, jamais deviner règle 2), normalise la valeur vers sa colonne typée (refus si elle
/// ne correspond pas au type d'axe, ou hors vocabulaire pour un <c>enum</c>) puis l'appende sous garde de
/// concurrence mono-valeur (RL-02). Rend l'identité de la ligne <c>document_axis_links</c> créée. Tenant-scopé
/// par la connexion.
/// </summary>
public sealed record SetAxisValueCommand : IRequest<Guid>
{
    /// <summary>Document géré porteur de la valeur (<c>managed_document_id</c>).</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Code machine de l'axe visé (résolu via <c>IAxisCatalog</c>).</summary>
    public required string AxisCode { get; init; }

    /// <summary>Valeur brute à normaliser selon le type de l'axe.</summary>
    public required string RawValue { get; init; }

    /// <summary>Provenance de la valeur (<c>agent|manual|ai|import|ocr</c>).</summary>
    public required string Source { get; init; }

    /// <summary>Identité de l'opérateur (attendue si <c>Source='manual'</c>).</summary>
    public string? OperatorIdentity { get; init; }

    /// <summary>Score de confiance [0..1] ; <see langword="null"/> si déterministe.</summary>
    public decimal? ConfidenceScore { get; init; }
}
