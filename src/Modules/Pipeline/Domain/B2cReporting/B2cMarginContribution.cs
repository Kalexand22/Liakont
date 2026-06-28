namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System;

/// <summary>
/// Contribution d'UN document B2C-marge (enchères) à l'agrégat e-reporting (flux 10.3). Donnée d'entrée
/// PURE de <see cref="B2cTransactionAggregationCalculator"/> : un document = sa marge <b>TTC</b> totale
/// (Σ honoraires acheteur + vendeur, F03 §2.4) portée à SON taux unique (taux mappé F03 du code TVA de
/// l'honoraire ; un document à honoraires de taux MIXTES est EXCLU en amont — F03 §2.5, jamais découpé ici).
/// Le taux et la nature TTC sont résolus par l'appelant (job tenant-scopé) ; ici, calcul pur en
/// <see cref="decimal"/> (CLAUDE.md n°1).
/// </summary>
public sealed record B2cMarginContribution
{
    /// <summary>Identifiant du document source (déclaration B2C-marge).</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Référence de la pièce source (ADR-0007) — pour la traçabilité reporting ↔ pièces.</summary>
    public required string SourceReference { get; init; }

    /// <summary>Jour de l'opération (grain d'agrégation, F03 §2.5).</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Devise ISO 4217 (ex. <c>EUR</c>).</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>Marge TTC du document = Σ honoraires acheteur + vendeur (TTC, F03 §2.4/§2.5).</summary>
    public required decimal MarginTtc { get; init; }

    /// <summary>Taux de TVA applicable (pourcentage, ex. <c>20.0</c>) — taux mappé F03 de l'honoraire (F03 §2.5).</summary>
    public required decimal RatePercent { get; init; }
}
