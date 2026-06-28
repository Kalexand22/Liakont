namespace Liakont.Modules.TenantSettings.Domain.Entities;

/// <summary>
/// Mentions de facturation du tenant (F12-A §3.4) — données de l'ENTREPRISE (conditions générales de
/// vente), portées sur la facture B2B : termes de paiement (EN 16931 BT-20) et mentions légales FR
/// obligatoires entre professionnels (BR-FR-05 : pénalités de retard PMD, indemnité forfaitaire de
/// recouvrement PMT, escompte ou son absence AAB).
/// </summary>
/// <remarks>
/// <para><strong>Aucun texte inventé (CLAUDE.md n°2/7) :</strong> le contenu est SAISI par le client /
/// son expert-comptable depuis la console ; le produit n'embarque aucune mention par défaut. Cette
/// entité ne fait que STOCKER ; l'exigence (blocage si absent sur une facture B2B FR) est appliquée
/// au CHECK par le pipeline, et la projection (BT-20 / notes BG-1) par le sérialiseur Factur-X et
/// <c>SuperPdpPayloadBuilder</c> (F16 §3.5).</para>
/// <para>Tous les champs sont nullables : <c>null</c> = mention non renseignée. Une chaîne vide ou
/// blanche est normalisée en <c>null</c> (mention absente).</para>
/// </remarks>
public sealed class BillingMentions
{
    private BillingMentions()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Termes / conditions de paiement (EN 16931 BT-20). Ex. « Paiement comptant exigible à la vente ».</summary>
    public string? PaymentTerms { get; private set; }

    /// <summary>Mention des pénalités de retard (note BR-FR-05, <c>subject_code</c> PMD).</summary>
    public string? LatePenaltyTerms { get; private set; }

    /// <summary>Mention de l'indemnité forfaitaire de recouvrement (note BR-FR-05, <c>subject_code</c> PMT).</summary>
    public string? RecoveryFeeTerms { get; private set; }

    /// <summary>Mention de l'escompte ou de son absence (note BR-FR-05, <c>subject_code</c> AAB).</summary>
    public string? DiscountTerms { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Crée des mentions de facturation. Tous les champs sont optionnels (défaut <c>null</c>).</summary>
    public static BillingMentions Create(
        Guid companyId,
        string? paymentTerms,
        string? latePenaltyTerms,
        string? recoveryFeeTerms,
        string? discountTerms)
    {
        return new BillingMentions
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            PaymentTerms = NormalizeOpaque(paymentTerms),
            LatePenaltyTerms = NormalizeOpaque(latePenaltyTerms),
            RecoveryFeeTerms = NormalizeOpaque(recoveryFeeTerms),
            DiscountTerms = NormalizeOpaque(discountTerms),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
        };
    }

    public static BillingMentions Reconstitute(
        Guid id,
        Guid companyId,
        string? paymentTerms,
        string? latePenaltyTerms,
        string? recoveryFeeTerms,
        string? discountTerms,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new BillingMentions
        {
            Id = id,
            CompanyId = companyId,
            PaymentTerms = paymentTerms,
            LatePenaltyTerms = latePenaltyTerms,
            RecoveryFeeTerms = recoveryFeeTerms,
            DiscountTerms = discountTerms,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    /// <summary>Met à jour les mentions. <c>null</c> (ou vide) efface la mention concernée.</summary>
    public void Update(
        string? paymentTerms,
        string? latePenaltyTerms,
        string? recoveryFeeTerms,
        string? discountTerms)
    {
        PaymentTerms = NormalizeOpaque(paymentTerms);
        LatePenaltyTerms = NormalizeOpaque(latePenaltyTerms);
        RecoveryFeeTerms = NormalizeOpaque(recoveryFeeTerms);
        DiscountTerms = NormalizeOpaque(discountTerms);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string? NormalizeOpaque(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
