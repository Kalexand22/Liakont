namespace Liakont.Agent.Contracts.Pivot;

using System;

/// <summary>
/// Un encaissement brut (F09 — e-reporting de paiement). L'agent transmet les paiements bruts ;
/// les agrégats jour × taux sont calculés sur la PLATEFORME (Pipeline). Décision D2 (2026-06-03) :
/// le e-reporting de paiement V1 ne produit que du Flux 10.4 (domestique) — aucun champ de
/// dérivation domestique/international requis ici (le Flux 10.2 arrivera en phase 2 avec sa règle
/// sourcée).
/// </summary>
public sealed class PivotPaymentDto
{
    /// <summary>Crée un encaissement pivot.</summary>
    /// <param name="paymentDate">Date d'encaissement.</param>
    /// <param name="amount">Montant encaissé (decimal).</param>
    /// <param name="method">Moyen de paiement (CB / chèque / espèces / virement — informatif).</param>
    /// <param name="relatedDocumentNumber">Numéro du document d'origine si rattachable (lettrage).</param>
    /// <param name="sourceReference">Référence de l'encaissement dans le système source.</param>
    public PivotPaymentDto(
        DateTime paymentDate,
        decimal amount,
        string? method = null,
        string? relatedDocumentNumber = null,
        string? sourceReference = null)
    {
        PaymentDate = paymentDate;
        Amount = amount;
        Method = method;
        RelatedDocumentNumber = relatedDocumentNumber;
        SourceReference = sourceReference;
    }

    /// <summary>Date d'encaissement.</summary>
    public DateTime PaymentDate { get; }

    /// <summary>Montant encaissé (decimal).</summary>
    public decimal Amount { get; }

    /// <summary>Moyen de paiement (informatif).</summary>
    public string? Method { get; }

    /// <summary>Numéro du document d'origine si rattachable (lettrage).</summary>
    public string? RelatedDocumentNumber { get; }

    /// <summary>Référence de l'encaissement dans le système source.</summary>
    public string? SourceReference { get; }
}
