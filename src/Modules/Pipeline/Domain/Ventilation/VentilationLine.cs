namespace Liakont.Modules.Pipeline.Domain.Ventilation;

using System;

/// <summary>
/// Une ligne de la ventilation TVA par taux d'un document, capturée au CHECK (ADR-0015) :
/// base taxable HT et TVA encaissables pour un <see cref="Rate"/> donné. C'est la SORTIE du mapping
/// validé projetée pour être requêtable — AUCUNE valeur n'est dérivée ni devinée (INV-VENTILATION-001).
/// Montants en <see cref="decimal"/> (CLAUDE.md n°1) ; le snapshot ne calcule rien.
/// </summary>
/// <remarks>
/// <see cref="Rate"/> est nullable : un taux non résolu au CHECK (table déférant au taux source ET
/// source sans taux explicite) reste <c>null</c> — l'agrégation de paiement (PIP03a) SUSPEND alors le
/// document concerné (un encaissement ne peut être ventilé par taux sans taux). Jamais de taux inventé.
/// </remarks>
public sealed record VentilationLine
{
    private VentilationLine()
    {
    }

    /// <summary>Taux de TVA (decimal, ≤ 4 décimales), ou <c>null</c> si non résolu au CHECK.</summary>
    public decimal? Rate { get; private init; }

    /// <summary>Base taxable HT encaissable pour ce taux (decimal, ≤ 2 décimales).</summary>
    public decimal TaxableBase { get; private init; }

    /// <summary>TVA encaissable pour ce taux (decimal, ≤ 2 décimales).</summary>
    public decimal VatAmount { get; private init; }

    /// <summary>
    /// Crée une ligne de ventilation, en rejetant toute valeur qui dépasserait l'échéance de stockage
    /// (numeric(18,2) / numeric(6,4)) : une troncature silencieuse altérerait une donnée fiscale (CLAUDE.md n°4).
    /// </summary>
    public static VentilationLine Create(decimal? rate, decimal taxableBase, decimal vatAmount)
    {
        if (rate.HasValue && decimal.Round(rate.Value, 4) != rate.Value)
        {
            throw new ArgumentException(
                $"Le taux ({rate}) dépasse 4 décimales : la colonne numeric(6,4) le tronquerait (intégrité de stockage, CLAUDE.md n°4).",
                nameof(rate));
        }

        RequireAmountScale(taxableBase, nameof(taxableBase));
        RequireAmountScale(vatAmount, nameof(vatAmount));

        return new VentilationLine
        {
            Rate = rate,
            TaxableBase = taxableBase,
            VatAmount = vatAmount,
        };
    }

    private static void RequireAmountScale(decimal value, string paramName)
    {
        if (decimal.Round(value, 2) != value)
        {
            throw new ArgumentException(
                $"Le montant '{paramName}' ({value}) dépasse 2 décimales : la colonne numeric(18,2) le tronquerait (intégrité de stockage, CLAUDE.md n°4).",
                paramName);
        }
    }
}
