namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Définit (upsert) les mentions de facturation du tenant courant (F12-A §3.4, BUG-26). Données de
/// l'entreprise (CGV) portées sur la facture B2B : termes de paiement (BT-20) et mentions légales FR
/// (BR-FR-05). Tous les champs sont optionnels ; une chaîne vide est traitée comme « non renseignée ».
/// Aucun contenu n'est inventé par le produit (CLAUDE.md n°2/7).
/// </summary>
public record SetBillingMentionsCommand : ICommand
{
    public string? PaymentTerms { get; init; }

    public string? LatePenaltyTerms { get; init; }

    public string? RecoveryFeeTerms { get; init; }

    public string? DiscountTerms { get; init; }
}
