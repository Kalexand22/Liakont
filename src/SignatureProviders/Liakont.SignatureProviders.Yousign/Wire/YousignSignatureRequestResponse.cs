namespace Liakont.SignatureProviders.Yousign.Wire;

/// <summary>
/// Réponse Yousign v3 à la création / relecture d'une <c>signature_request</c> (champs strictement nécessaires
/// au plug-in). Type « fil » INTERNE : il ne traverse jamais l'abstraction (INV-YOUSIGN-2).
/// </summary>
internal sealed record YousignSignatureRequestResponse
{
    /// <summary>Identifiant de la demande côté Yousign (référence du fournisseur).</summary>
    public string? Id { get; init; }

    /// <summary>Statut courant Yousign (<c>draft</c>, <c>ongoing</c>, <c>done</c>, <c>declined</c>, <c>expired</c>…).</summary>
    public string? Status { get; init; }
}
