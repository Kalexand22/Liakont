namespace Liakont.Agent.Contracts.Pivot;

using System;
using System.Collections.Generic;

/// <summary>
/// Une ligne du document (EN 16931 BG-25). DTO pur : aucun calcul, les montants viennent de la
/// source (F01-F02 §3.7 règle 2).
/// </summary>
public sealed class PivotLineDto
{
    /// <summary>Crée une ligne pivot.</summary>
    /// <param name="description">Libellé de la ligne (EN 16931 BT-153). Obligatoire.</param>
    /// <param name="netAmount">Montant HT de la ligne (EN 16931 BT-131), decimal.</param>
    /// <param name="quantity">Quantité (EN 16931 BT-129). Défaut 1.</param>
    /// <param name="unitPriceNet">Prix unitaire HT (EN 16931 BT-146). Absent = <c>null</c>.</param>
    /// <param name="sourceRegimeCodes">
    /// Régimes TVA de la source, BRUTS — une COLLECTION par ligne (ADR-0004 D3-1), jamais une
    /// simple chaîne : une source peut porter un couple de codes (NAV) ou plusieurs taxes sur une
    /// même ligne (Axelor). Le moteur F3 mappe la collection et scinde en lignes pivot si EN 16931
    /// l'exige (BG-30 = 1 catégorie/ligne). L'adaptateur n'interprète JAMAIS (CLAUDE.md n°2).
    /// </param>
    /// <param name="taxes">Ventilation de TVA de la ligne (catégorie/VATEX remplis par le mapping plateforme).</param>
    /// <param name="sourceLineRef">Référence de la ligne dans le système source (traçabilité).</param>
    /// <param name="sourceData">Données source brutes utiles à la traçabilité (JSON), p. ex. montants non arrondis.</param>
    /// <param name="unitCode">
    /// Unité de mesure de la quantité (EN 16931 BT-130, codes UN/ECE Rec 20). Champ ADDITIF optionnel
    /// (ADR-0004 §5 / ADR-0007 : ajouté en FIN du contrat). Absent = <c>null</c> : à l'émission, chaque
    /// PA/format applique son unité neutre par défaut (<c>C62</c> « one »). Une chaîne vide/blanche est
    /// normalisée en <c>null</c> (équivalent « absent », empreinte canonique inchangée). L'adaptateur
    /// recopie le code source sans l'interpréter (CLAUDE.md n°2) — aucune table d'unités n'est inventée.
    /// </param>
    public PivotLineDto(
        string description,
        decimal netAmount,
        decimal quantity = 1m,
        decimal? unitPriceNet = null,
        IReadOnlyList<string>? sourceRegimeCodes = null,
        IReadOnlyList<PivotLineTaxDto>? taxes = null,
        string? sourceLineRef = null,
        string? sourceData = null,
        string? unitCode = null)
    {
        Description = description;
        NetAmount = netAmount;
        Quantity = quantity;
        UnitPriceNet = unitPriceNet;
        SourceRegimeCodes = sourceRegimeCodes ?? Array.Empty<string>();
        Taxes = taxes ?? Array.Empty<PivotLineTaxDto>();
        SourceLineRef = sourceLineRef;
        SourceData = sourceData;

        // Normalisation de SURFACE uniquement (jamais d'interprétation du code, CLAUDE.md n°2) : on borne
        // les espaces de bord — un code padded (« C62 ») casserait l'appariement de la liste UN/ECE au
        // schématron EN 16931 côté PA et brouillerait l'empreinte canonique pour un simple écart d'espacement.
        var trimmedUnitCode = unitCode?.Trim();
        UnitCode = string.IsNullOrEmpty(trimmedUnitCode) ? null : trimmedUnitCode;
    }

    /// <summary>Libellé de la ligne (EN 16931 BT-153).</summary>
    public string Description { get; }

    /// <summary>Montant HT de la ligne (EN 16931 BT-131), decimal.</summary>
    public decimal NetAmount { get; }

    /// <summary>Quantité (EN 16931 BT-129).</summary>
    public decimal Quantity { get; }

    /// <summary>Prix unitaire HT (EN 16931 BT-146).</summary>
    public decimal? UnitPriceNet { get; }

    /// <summary>Régimes TVA de la source, BRUTS — collection par ligne (ADR-0004 D3-1).</summary>
    public IReadOnlyList<string> SourceRegimeCodes { get; }

    /// <summary>Ventilation de TVA de la ligne (résultat du mapping plateforme pour catégorie/VATEX).</summary>
    public IReadOnlyList<PivotLineTaxDto> Taxes { get; }

    /// <summary>Référence de la ligne dans le système source (traçabilité).</summary>
    public string? SourceLineRef { get; }

    /// <summary>Données source brutes utiles à la traçabilité (JSON).</summary>
    public string? SourceData { get; }

    /// <summary>
    /// Unité de mesure de la quantité (EN 16931 BT-130, codes UN/ECE Rec 20). Champ additif optionnel ;
    /// <c>null</c> = unité neutre par défaut à l'émission (<c>C62</c>). Voir le constructeur.
    /// </summary>
    public string? UnitCode { get; }
}
