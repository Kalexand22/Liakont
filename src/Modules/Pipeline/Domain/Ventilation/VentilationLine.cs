namespace Liakont.Modules.Pipeline.Domain.Ventilation;

/// <summary>
/// Une ligne de la ventilation TVA par taux d'un document, capturée au CHECK (ADR-0015) :
/// base taxable HT, TVA et catégorie UNCL5305 pour un <see cref="Rate"/> donné. C'est la SORTIE du mapping
/// validé projetée pour être requêtable — AUCUNE valeur n'est dérivée ni devinée (INV-VENTILATION-001).
/// Montants en <see cref="decimal"/> (CLAUDE.md n°1), sérialisés en CHAÎNES invariantes (jamais de float) ;
/// le snapshot ne calcule rien.
/// </summary>
/// <remarks>
/// <see cref="Rate"/> est nullable : un taux non résolu au CHECK (table déférant au taux source ET
/// source sans taux explicite) reste <c>null</c> — l'agrégation de paiement (PIP03a) SUSPEND alors le
/// document concerné (un encaissement ne peut être ventilé par taux sans taux). Jamais de taux inventé.
/// <see cref="Category"/> (UNCL5305 : S/AA/AAA/Z/E/AE/G/K/O) est capturée DÈS LE CHECK pour que l'exclusion
/// SOURCÉE de l'autoliquidation (AE — F09 §2) reste applicable depuis ce snapshot APPEND-ONLY, sans re-dériver
/// depuis le coffre WORM ; <c>null</c> tant que le mapping ne l'a pas posée.
/// </remarks>
public sealed record VentilationLine
{
    private VentilationLine()
    {
    }

    /// <summary>Taux de TVA (decimal), ou <c>null</c> si non résolu au CHECK.</summary>
    public decimal? Rate { get; private init; }

    /// <summary>Base taxable HT encaissable pour ce taux (decimal).</summary>
    public decimal TaxableBase { get; private init; }

    /// <summary>TVA encaissable pour ce taux (decimal).</summary>
    public decimal VatAmount { get; private init; }

    /// <summary>Catégorie TVA UNCL5305 (nom d'énumération) issue du mapping validé, ou <c>null</c> si non posée.</summary>
    public string? Category { get; private init; }

    /// <summary>
    /// Crée une ligne de ventilation. Les montants sont conservés tels quels (precision préservée par la
    /// sérialisation en chaîne jsonb) ; aucune valeur n'est arrondie ni recalculée ici (INV-VENTILATION-001).
    /// </summary>
    public static VentilationLine Create(decimal? rate, decimal taxableBase, decimal vatAmount, string? category = null) =>
        new()
        {
            Rate = rate,
            TaxableBase = taxableBase,
            VatAmount = vatAmount,
            Category = category,
        };
}
