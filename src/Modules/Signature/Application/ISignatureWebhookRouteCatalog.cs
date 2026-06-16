namespace Liakont.Modules.Signature.Application;

using Liakont.Modules.Signature.Domain.Entities;

/// <summary>
/// Catalogue SYSTÈME des routes de webhook (ADR-0029 §2 ; INV-YOUSIGN-3). Résout un handle de tenant OPAQUE
/// vers le tenant cible — pur aiguillage d'infra, AUCUNE donnée métier, AUCUN lookup cross-tenant pré-scope
/// (modèle <c>ICompanyTenantLookup</c>, contrainte <c>UNIQUE</c> sur l'<c>opaque_ref</c>). Interrogé sur la
/// base SYSTÈME, AVANT toute ouverture de scope tenant.
/// </summary>
public interface ISignatureWebhookRouteCatalog
{
    /// <summary>Résout un handle opaque vers sa route (tenant + société + type), ou <c>null</c> si inconnu.</summary>
    /// <param name="opaqueRef">Handle opaque extrait de l'URL de webhook.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<SignatureWebhookRoute?> ResolveAsync(string opaqueRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enregistre (ou remplace) une route pour un tenant. Le handle opaque doit être UNIQUE dans le catalogue
    /// système (un même handle ne peut pas router vers deux tenants).
    /// </summary>
    /// <param name="route">Route à enregistrer.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task RegisterAsync(SignatureWebhookRoute route, CancellationToken cancellationToken = default);
}
