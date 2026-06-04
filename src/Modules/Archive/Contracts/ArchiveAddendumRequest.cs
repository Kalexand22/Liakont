namespace Liakont.Modules.Archive.Contracts;

using System;

/// <summary>
/// Demande d'ajout d'un ADDENDUM à un paquet existant (TRK05 §4-§5) : une pièce récupérée APRÈS coup
/// (tax-report DGFiP, PDF réconcilié) ne réécrit JAMAIS le paquet (WORM) — elle est ajoutée comme un
/// nouveau fichier, décrit par un manifest-addendum, et CHAÎNÉE sur la chaîne principale du tenant
/// (<c>chain_hash(N+1) = SHA256(chain_hash(N) + addendum_hash)</c>). Le paquet cible est localisé par son
/// numéro et sa date d'émission (même arborescence que le paquet initial).
/// </summary>
public sealed class ArchiveAddendumRequest
{
    /// <summary>Identifiant du document auquel l'addendum se rattache.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Numéro du document (localise le répertoire du paquet).</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>Date d'émission du document (année/mois du répertoire du paquet).</summary>
    public required DateOnly IssueDate { get; init; }

    /// <summary>Nature de l'addendum (ex. « tax-report », « pdf-reconcilie »), tracée dans le manifest-addendum.</summary>
    public required string Kind { get; init; }

    /// <summary>La pièce ajoutée (exacte) — un seul fichier par addendum.</summary>
    public required ArchiveAttachment Attachment { get; init; }
}
