namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// Demande de signature soumise à un <see cref="ISignatureProvider"/> (ADR-0027 §2). Surface STABLE et
/// indépendante du fournisseur : le payload propre au fournisseur (champs Yousign, capture Wacom, octets
/// du document, signataires détaillés) vit DANS le plug-in (SIG07/SIG08), jamais dans ce contrat —
/// aucun type HTTP ne traverse l'abstraction (INV-SIGPROV-8). La demande ne porte que ce qui pilote le
/// gating par capacités : le tenant, le document visé, le niveau et la localisation demandés.
/// </summary>
public sealed record SignatureRequest
{
    /// <summary>Tenant propriétaire (clé d'isolation <c>company_id</c>). Jamais vide.</summary>
    public required string CompanyId { get; init; }

    /// <summary>Identifiant du document à faire signer (tenant-scopé). Jamais vide.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Niveau de preuve demandé (issu du PARAMÉTRAGE TENANT, F17 §7 — jamais un défaut produit inventé).</summary>
    public required SignatureLevel RequestedLevel { get; init; }

    /// <summary>Localisation demandée (à distance / sur place).</summary>
    public required SignatureMode RequestedMode { get; init; }

    /// <summary>
    /// Hash des octets exacts de l'artefact à lier, le cas échéant (scellement eIDAS art. 26 d). Le calcul
    /// et la vérification du hash de binding sont une primitive du plug-in sur place (ADR-0030, SIG08) ;
    /// ce contrat ne le transporte qu'optionnellement. <c>null</c> = pas de liaison de hash demandée.
    /// </summary>
    public string? DocumentHash { get; init; }
}
