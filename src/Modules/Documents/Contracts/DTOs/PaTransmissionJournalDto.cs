namespace Liakont.Modules.Documents.Contracts.DTOs;

using System;

/// <summary>
/// Projection en lecture d'un fait d'audit de JOURNALISATION D'ENVOI PA (item FX06, F16 §7), retrouvé par sa
/// CLÉ D'IDEMPOTENCE recherchable. Surface les colonnes additives de transmission (compte/plug-in PA,
/// horodatages, empreinte de l'artefact transmis) ; la réponse PA brute reste consultable via le snapshot de
/// l'événement. Reflète l'immuabilité de la piste d'audit : lecture seule de l'historique, jamais une mutation.
/// </summary>
public sealed record PaTransmissionJournalDto
{
    /// <summary>Identifiant de l'entrée d'audit.</summary>
    public required Guid EventId { get; init; }

    /// <summary>Document dont l'envoi a été journalisé.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Horodatage de l'événement d'audit (UTC).</summary>
    public required DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Clé d'idempotence recherchable de l'envoi à la PA.</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>Compte de la Plateforme Agréée par lequel le document a été transmis.</summary>
    public required string PaAccount { get; init; }

    /// <summary>Identifiant du type de plug-in PA ayant transmis.</summary>
    public required string PaPluginId { get; init; }

    /// <summary>Horodatage UTC de l'envoi de la requête de transmission.</summary>
    public required DateTimeOffset PaRequestUtc { get; init; }

    /// <summary>Horodatage UTC de la réponse de la PA.</summary>
    public required DateTimeOffset PaResponseUtc { get; init; }

    /// <summary>Empreinte SHA-256 de l'artefact réellement transmis (le Factur-X).</summary>
    public required string TransmittedArtifactHash { get; init; }
}
