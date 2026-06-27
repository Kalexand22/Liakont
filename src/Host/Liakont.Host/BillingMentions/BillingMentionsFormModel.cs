namespace Liakont.Host.BillingMentions;

/// <summary>
/// Saisie éditable des mentions de facturation du tenant (BUG-26, F12-A §3.4). Les quatre champs sont du
/// TEXTE LIBRE multi-ligne lié aux zones du formulaire ; une chaîne vide = « non renseigné » = <c>null</c>
/// côté commande (aucun contenu n'est inventé par le produit — CLAUDE.md n°2/7). Mutable (instance partagée
/// avec la page).
/// </summary>
public sealed class BillingMentionsFormModel
{
    /// <summary>Termes de paiement (EN 16931 BT-20) : texte libre ou vide (non renseigné).</summary>
    public string? PaymentTerms { get; set; }

    /// <summary>Pénalités de retard (mention légale FR) : texte libre ou vide (non renseigné).</summary>
    public string? LatePenaltyTerms { get; set; }

    /// <summary>Indemnité forfaitaire de recouvrement (mention légale FR) : texte libre ou vide (non renseigné).</summary>
    public string? RecoveryFeeTerms { get; set; }

    /// <summary>Escompte ou son absence (mention légale FR) : texte libre ou vide (non renseigné).</summary>
    public string? DiscountTerms { get; set; }
}
