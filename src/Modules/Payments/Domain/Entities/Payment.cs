namespace Liakont.Modules.Payments.Domain.Entities;

using System;

/// <summary>
/// Encaissement BRUT reçu de l'agent (F09 — e-reporting de paiement, item TRK04). Agrégat racine simple du
/// module <c>Payments</c> : il conserve le paiement tel qu'extrait par la source (montant en
/// <see cref="decimal"/>, CLAUDE.md n°1), SANS interprétation fiscale. L'agrégation jour × taux et la
/// transmission arrivent avec le pipeline (PIP03) — ce module porte le MODÈLE et la PERSISTANCE (TRK04).
/// Il vit dans la base DU TENANT (database-per-tenant, blueprint §7) : aucune colonne de tenant, l'isolation
/// est assurée par la connexion. Rétention 10 ans, jamais purgé automatiquement (F06 §6 / F09).
/// </summary>
/// <remarks>
/// V1 = Flux 10.4 domestique uniquement (F09 amendement D2 du 2026-06-03) : aucune dérivation
/// domestique/international ici. Le rattachement au document d'origine (<see cref="RelatedDocumentNumber"/>)
/// est INDICATIF (lettrage) — un règlement sans rattachement reste un paiement valide (F09 §5.4).
/// </remarks>
public sealed class Payment
{
    private Payment()
    {
    }

    /// <summary>Identifiant du paiement dans la passerelle.</summary>
    public Guid Id { get; private set; }

    /// <summary>Date d'encaissement (l'e-reporting de paiement est agrégé par jour — F09 §2).</summary>
    public DateOnly PaymentDate { get; private set; }

    /// <summary>Montant encaissé, <see cref="decimal"/> (peut être négatif : trop-perçu / remboursement — F09 §5.4).</summary>
    public decimal Amount { get; private set; }

    /// <summary>Moyen de paiement (CB / chèque / espèces / virement — informatif). Absent = <c>null</c>.</summary>
    public string? Method { get; private set; }

    /// <summary>Numéro du document d'origine si rattachable (lettrage indicatif). Absent = <c>null</c>.</summary>
    public string? RelatedDocumentNumber { get; private set; }

    /// <summary>Référence de l'encaissement dans le système source (audit / réconciliation). Absent = <c>null</c>.</summary>
    public string? SourceReference { get; private set; }

    /// <summary>Date de réception du paiement par la plateforme (UTC).</summary>
    public DateTimeOffset ReceivedUtc { get; private set; }

    /// <summary>
    /// Crée un paiement à partir des données brutes d'un encaissement reçu de l'agent (PivotPaymentDto,
    /// projeté par <c>PivotPaymentMapper</c>). Le montant est conservé tel que calculé par la source.
    /// </summary>
    public static Payment Create(
        Guid id,
        DateOnly paymentDate,
        decimal amount,
        string? method,
        string? relatedDocumentNumber,
        string? sourceReference,
        DateTimeOffset receivedUtc)
    {
        MonetaryScale.RequireAmount(amount, nameof(amount));

        return new Payment
        {
            Id = id,
            PaymentDate = paymentDate,
            Amount = amount,
            Method = NullIfBlank(method),
            RelatedDocumentNumber = NullIfBlank(relatedDocumentNumber),
            SourceReference = NullIfBlank(sourceReference),
            ReceivedUtc = receivedUtc,
        };
    }

    /// <summary>Reconstitue un paiement depuis la persistance (lecture).</summary>
    public static Payment Reconstitute(
        Guid id,
        DateOnly paymentDate,
        decimal amount,
        string? method,
        string? relatedDocumentNumber,
        string? sourceReference,
        DateTimeOffset receivedUtc)
    {
        return new Payment
        {
            Id = id,
            PaymentDate = paymentDate,
            Amount = amount,
            Method = method,
            RelatedDocumentNumber = relatedDocumentNumber,
            SourceReference = sourceReference,
            ReceivedUtc = receivedUtc,
        };
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
