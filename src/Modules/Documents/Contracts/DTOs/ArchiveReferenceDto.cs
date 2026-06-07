namespace Liakont.Modules.Documents.Contracts.DTOs;

using System;

/// <summary>
/// Référence d'archive d'un document (API01a, détail GET /documents/{id}) : le paquet d'archive WORM
/// alimenté à l'émission (TRK05). Présence dans le coffre + empreintes chaînées enregistrées. La
/// vérification cryptographique COMPLÈTE du coffre (re-calcul des empreintes, preuves d'ancrage) est une
/// action à la demande exposée séparément (API03, <c>POST /api/v1/archive/verify</c>) — non re-jouée à
/// chaque consultation de détail.
/// </summary>
public sealed record ArchiveReferenceDto
{
    /// <summary>Chemin du paquet d'archive (clé d'idempotence du coffre).</summary>
    public required string PackagePath { get; init; }

    /// <summary>Empreinte du paquet (intégrité du contenu archivé).</summary>
    public required string PackageHash { get; init; }

    /// <summary>Empreinte chaînée (chaînage inviolable de la séquence d'archives — TRK05).</summary>
    public required string ChainHash { get; init; }

    /// <summary>Horodatage d'archivage.</summary>
    public required DateTimeOffset ArchivedUtc { get; init; }
}
