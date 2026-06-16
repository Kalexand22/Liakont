namespace Liakont.Modules.Documents.Contracts.Lifecycle;

using System;

/// <summary>
/// Données d'un fait d'audit de JOURNALISATION D'ENVOI PA (item FX06/FX07, F16 §7), consigné par le pipeline
/// après une transmission réussie d'un Factur-X. Reflète les colonnes additives de
/// <c>documents.document_events</c> (FX06). L'horodatage de l'événement d'audit lui-même est posé par
/// l'implémentation (horloge du module) ; cette entrée ne porte que les données métier de la transmission.
/// Aucun secret (CLAUDE.md n°10/18) : ni mot de passe SMTP ni clé API.
/// </summary>
public sealed record PaTransmissionJournalEntry
{
    /// <summary>Document dont l'envoi est journalisé (déjà émis).</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Compte de la Plateforme Agréée par lequel le document a été transmis (identifiant non sensible).</summary>
    public required string PaAccount { get; init; }

    /// <summary>Identifiant du type de plug-in PA ayant transmis (clé de registre, ex. « generique »).</summary>
    public required string PaPluginId { get; init; }

    /// <summary>Horodatage UTC de l'envoi de la requête de transmission.</summary>
    public required DateTimeOffset PaRequestUtc { get; init; }

    /// <summary>Horodatage UTC de la réponse de la PA.</summary>
    public required DateTimeOffset PaResponseUtc { get; init; }

    /// <summary>Empreinte SHA-256 de l'artefact réellement transmis (le Factur-X scellé).</summary>
    public required string TransmittedArtifactHash { get; init; }

    /// <summary>Clé d'idempotence recherchable de l'envoi (BT-1, numéro de document — F16 §7).</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>Réponse PA brute, conservée pour l'audit (jamais un secret).</summary>
    public required string PaResponseSnapshot { get; init; }

    /// <summary>Détail opérateur (français) du fait d'audit.</summary>
    public required string Detail { get; init; }
}
