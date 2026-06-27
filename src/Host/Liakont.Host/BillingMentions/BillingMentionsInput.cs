namespace Liakont.Host.BillingMentions;

/// <summary>
/// Valeurs brutes du formulaire des mentions de facturation transmises au service à l'enregistrement
/// (BUG-26, F12-A §3.4). Le service normalise les chaînes vides en <c>null</c> (« non renseigné ») avant
/// d'émettre <c>SetBillingMentionsCommand</c> ; aucun contenu n'est inventé par le produit (CLAUDE.md n°2/7).
/// </summary>
public sealed record BillingMentionsInput
{
    /// <summary>Termes de paiement (BT-20) : texte libre ou vide.</summary>
    public string? PaymentTerms { get; init; }

    /// <summary>Pénalités de retard (mention légale FR) : texte libre ou vide.</summary>
    public string? LatePenaltyTerms { get; init; }

    /// <summary>Indemnité forfaitaire de recouvrement (mention légale FR) : texte libre ou vide.</summary>
    public string? RecoveryFeeTerms { get; init; }

    /// <summary>Escompte ou son absence (mention légale FR) : texte libre ou vide.</summary>
    public string? DiscountTerms { get; init; }
}
