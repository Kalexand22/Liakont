namespace Liakont.Modules.Archive.Domain;

using System;

/// <summary>
/// Résultat d'un ancrage temporel (TRK06). Quand <see cref="IsAnchored"/> est <c>true</c>,
/// <see cref="Proof"/> porte la preuve à archiver dans le coffre (jeton RFC 3161, fichier .ots) et
/// <see cref="AnchoredUtc"/> l'instant attesté par le service. Quand il est <c>false</c> (NoAnchor),
/// il n'y a pas de preuve : l'intégrité reste portée par la chaîne de hashes.
/// </summary>
/// <param name="Method">La méthode qui a produit (ou non) la preuve.</param>
/// <param name="IsAnchored"><c>true</c> si une preuve a été produite et doit être archivée.</param>
/// <param name="Proof">Octets de la preuve à archiver (<c>null</c> si non ancré).</param>
/// <param name="ProofContentType">Type MIME de la preuve (ex. <c>application/timestamp-token</c>).</param>
/// <param name="AnchoredUtc">Instant attesté par le service d'horodatage (UTC), ou <c>null</c> si non ancré.</param>
/// <param name="Detail">Message français explicitant l'issue (motif d'absence d'ancrage, etc.).</param>
public sealed record TimestampAnchorResult(
    TimestampAnchorMethod Method,
    bool IsAnchored,
    byte[]? Proof,
    string? ProofContentType,
    DateTimeOffset? AnchoredUtc,
    string Detail)
{
    /// <summary>Aucun ancrage produit (NoAnchor) : la chaîne de hashes reste l'intégrité de référence.</summary>
    public static TimestampAnchorResult NotAnchored(TimestampAnchorMethod method, string detail) =>
        new(method, IsAnchored: false, Proof: null, ProofContentType: null, AnchoredUtc: null, detail);
}
