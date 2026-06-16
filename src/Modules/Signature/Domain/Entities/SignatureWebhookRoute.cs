namespace Liakont.Modules.Signature.Domain.Entities;

/// <summary>
/// Route de webhook : associe un HANDLE de tenant OPAQUE (non devinable) au tenant cible (ADR-0029 §2 ;
/// INV-YOUSIGN-3). Catalogue SYSTÈME d'infra (modèle <c>ICompanyTenantLookup</c>, contrainte <c>UNIQUE</c> sur
/// l'<see cref="OpaqueRef"/>), interrogé sur la base SYSTÈME : il ne résout QU'UN aiguillage
/// <c>{opaqueRef} → tenant</c>, AUCUNE donnée métier, AUCUN lookup cross-tenant pré-scope. L'<see cref="OpaqueRef"/>
/// n'est PAS un secret (le HMAC reste exigé) — il route sans scanner ni charger le compte de signature.
/// </summary>
public sealed record SignatureWebhookRoute
{
    /// <summary>Handle opaque non devinable extrait de l'URL de webhook.</summary>
    public required string OpaqueRef { get; init; }

    /// <summary>Identifiant du tenant cible (pour ouvrir le scope tenant).</summary>
    public required string TenantId { get; init; }

    /// <summary>Société du tenant (clé <c>company_id</c>, défense en profondeur du scoping).</summary>
    public required Guid CompanyId { get; init; }

    /// <summary>Type de fournisseur attendu sur cette route (ex. « Yousign »).</summary>
    public required string ProviderType { get; init; }
}
