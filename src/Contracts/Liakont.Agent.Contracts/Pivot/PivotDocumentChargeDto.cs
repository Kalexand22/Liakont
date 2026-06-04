namespace Liakont.Agent.Contracts.Pivot;

using System;
using System.Collections.Generic;

/// <summary>
/// Charge ou remise au niveau du document (EN 16931 BG-20 remises / BG-21 charges). Porte les
/// taxes NON-TVA (écotaxe, taxes parafiscales) et les remises globales (ADR-0004 D3-8, D4
/// famille 1). DTO pur : aucun calcul, les montants viennent de la source.
/// </summary>
public sealed class PivotDocumentChargeDto
{
    /// <summary>Crée une charge ou remise de niveau document.</summary>
    /// <param name="isCharge"><c>true</c> = charge (BG-21) ; <c>false</c> = remise (BG-20).</param>
    /// <param name="amount">Montant de la charge ou remise (decimal), HT.</param>
    /// <param name="reason">Motif en clair (ex. « éco-contribution »).</param>
    /// <param name="reasonCode">Code motif source (brut), si présent.</param>
    /// <param name="sourceRegimeCodes">
    /// Régimes/taxes source bruts associés (collection — même principe qu'une ligne, ADR-0004 D3-1) ;
    /// l'interprétation (catégorie TVA, hors base) vit dans le mapping/validation de la plateforme.
    /// </param>
    public PivotDocumentChargeDto(
        bool isCharge,
        decimal amount,
        string? reason = null,
        string? reasonCode = null,
        IReadOnlyList<string>? sourceRegimeCodes = null)
    {
        IsCharge = isCharge;
        Amount = amount;
        Reason = reason;
        ReasonCode = reasonCode;
        SourceRegimeCodes = sourceRegimeCodes ?? Array.Empty<string>();
    }

    /// <summary><c>true</c> = charge (BG-21) ; <c>false</c> = remise (BG-20).</summary>
    public bool IsCharge { get; }

    /// <summary>Montant de la charge ou remise (decimal), HT.</summary>
    public decimal Amount { get; }

    /// <summary>Motif en clair.</summary>
    public string? Reason { get; }

    /// <summary>Code motif source (brut).</summary>
    public string? ReasonCode { get; }

    /// <summary>Régimes/taxes source bruts associés (collection).</summary>
    public IReadOnlyList<string> SourceRegimeCodes { get; }
}
