namespace Liakont.Host.Documents;

using System;

/// <summary>
/// Option du sélecteur de document de remplacement (WEB03c, action <c>supersede</c>) : un document du tenant
/// courant proposé à l'opérateur pour lier un document rejeté à son remplaçant. Projection de lecture
/// (<c>DocumentSummaryDto</c>) limitée à ce qu'affiche le sélecteur — aucune règle métier. Montant en
/// <see cref="decimal"/> (CLAUDE.md n°1).
/// </summary>
internal sealed record DocumentReplacementCandidate
{
    /// <summary>Identifiant du document candidat (transmis tel quel au port <c>supersede</c>).</summary>
    public required Guid Id { get; init; }

    /// <summary>Numéro du document (BT-1) tel que créé par le logiciel source.</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>Nom de l'acheteur, ou <c>null</c> si non renseigné.</summary>
    public string? CustomerName { get; init; }

    /// <summary>Date d'émission du document.</summary>
    public required DateOnly IssueDate { get; init; }

    /// <summary>Montant TTC du document.</summary>
    public required decimal TotalGross { get; init; }

    /// <summary>État courant du document (libellé du module), pour situer le candidat dans le sélecteur.</summary>
    public required string State { get; init; }
}
