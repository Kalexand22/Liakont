namespace Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Mentions de facturation en lecture (F12-A §3.4) — données de l'entreprise (CGV) portées sur la facture
/// B2B : termes de paiement (BT-20) + mentions légales FR (BR-FR-05 : PMD/PMT/AAB). Tous les champs sont
/// nullables (<c>null</c> = mention non renseignée). Aucun contenu n'est inventé par le produit.
/// </summary>
public record BillingMentionsDto
{
    public required Guid Id { get; init; }

    public required Guid CompanyId { get; init; }

    public string? PaymentTerms { get; init; }

    public string? LatePenaltyTerms { get; init; }

    public string? RecoveryFeeTerms { get; init; }

    public string? DiscountTerms { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
