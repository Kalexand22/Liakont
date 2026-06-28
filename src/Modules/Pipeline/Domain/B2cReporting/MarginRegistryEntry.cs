namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System;

/// <summary>
/// Entrée du registre de la MARGE à déclarer (Livrable 2, projection <c>pipeline.margin_registry</c>) : la
/// marge d'UN document au régime de la marge, ramenée HT + TVA sur marge, pour l'aide à la déclaration de TVA
/// (CA3 — le commissaire-priseur déclare lui-même la TVA sur sa marge, art. 297 E / F03 §2.5). Le grain est le
/// DOCUMENT (un doc = un taux unique, F03 §2.3 pt 2) : c'est une PROJECTION recalculable (upsert sur
/// <see cref="DocumentId"/>), JAMAIS une piste d'audit (≠ <see cref="B2cMarginEmissionEntry"/>, WORM).
/// <para>AUCUN chiffre inventé (CLAUDE.md n°2) : la marge (Σ honoraires acheteur + vendeur) et le passage HT
/// (<c>HT = arrondi(TTC / (1 + taux))</c>, <c>TVA = TTC − HT</c>) viennent des cœurs purs sourcés
/// (<see cref="B2cMarginDocumentResolver"/>, <see cref="B2cTransactionAggregationCalculator.ToHt"/>, F03 §2.4/§2.5) ;
/// le taux vient de la table validée du tenant. Montants en <see cref="decimal"/> (CLAUDE.md n°1).</para>
/// </summary>
public sealed record MarginRegistryEntry
{
    /// <summary>Document marge source (clé d'upsert — un doc = un taux unique).</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Date d'émission du document (EN 16931 BT-2) — fait générateur, grain de regroupement par mois.</summary>
    public required DateOnly IssueDate { get; init; }

    /// <summary>Devise ISO 4217 de la marge (EN 16931 BT-5).</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>Taux de TVA unique de la vente, en pourcentage (ex. <c>20</c> pour 20 %).</summary>
    public required decimal VatRate { get; init; }

    /// <summary>Base HT de la marge ramenée au taux (<c>HT = arrondi(marge TTC / (1 + taux))</c>), half-up 2 décimales.</summary>
    public required decimal MarginBaseHt { get; init; }

    /// <summary>TVA sur la marge (<c>TVA = marge TTC − base HT</c>) — montant à reporter en déclaration de TVA.</summary>
    public required decimal MarginVat { get; init; }
}
