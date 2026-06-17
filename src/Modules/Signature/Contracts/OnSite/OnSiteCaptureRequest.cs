namespace Liakont.Modules.Signature.Contracts.OnSite;

using System;

/// <summary>
/// Objet IMMUABLE posté par le client soft de signature sur place (capteur Wacom, racine
/// <c>clients/OnSiteSignature/</c>) vers le proxy <c>OnSiteCapture</c> de la plateforme (ADR-0030 §3 ;
/// F17 §6). Le client est un PUR CAPTEUR : il ne porte AUCUNE logique métier, AUCUN accès base ; toute
/// décision reste côté plateforme. Le <c>company_id</c> n'est JAMAIS dans ce payload — le proxy le résout
/// du principal authentifié (tenant-scoping serveur, ADR-0030 §3, CLAUDE.md n°9).
/// <para>
/// Identité (ADR-0030 §5) : <see cref="DeclaredOperatorIdentity"/> est l'identité opérateur DÉCLARÉE,
/// purement INDICATIVE et NON PROBANTE — elle n'est JAMAIS retenue comme signataire. Le signataire
/// (mandant identifié en personne par la SVV) est résolu par un mécanisme de liaison VÉRIFIÉ séparé,
/// côté serveur, jamais depuis ce payload.
/// </para>
/// </summary>
public sealed record OnSiteCaptureRequest
{
    /// <summary>Document signé sur place (auto-facture 389 / mandat). Re-vérifié <c>document_id → company_id</c> côté serveur.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>
    /// Empreinte de binding SIGNÉE par le client = SHA-256 (hex minuscule) des OCTETS EXACTS de l'artefact
    /// Factur-X scellé reçu (ADR-0030 §4). La plateforme re-hashe son artefact stocké et vérifie l'égalité
    /// (<c>re-hash == hash signé</c>) : un client qui aurait hashé d'autres octets est rejeté.
    /// </summary>
    public required string SignedBindingHash { get; init; }

    /// <summary>Forme de stockage de la signature (FSS) chiffrée côté client (Base64) — preuve, jamais un gabarit biométrique (ADR-0030 §8).</summary>
    public required string EncryptedFssBase64 { get; init; }

    /// <summary>Rendu PNG de la signature manuscrite (Base64) — pièce de preuve rapatriée en WORM.</summary>
    public required string SignatureImagePngBase64 { get; init; }

    /// <summary>
    /// Identité opérateur DÉCLARÉE par le client (poste de la salle des ventes) — INDICATIVE et NON PROBANTE.
    /// JAMAIS retenue comme <c>SignerIdentity</c> (ADR-0030 §5 ; test d'usurpation). Peut être <c>null</c>.
    /// </summary>
    public string? DeclaredOperatorIdentity { get; init; }

    /// <summary>Horodatage de capture côté client (UTC), conservé dans la preuve.</summary>
    public required DateTimeOffset CapturedAtUtc { get; init; }
}
