namespace Liakont.Modules.Pipeline.Domain.Margin;

/// <summary>
/// Marge e-reporting B2C d'UN lot (B2C-09b, F03 §2.4). La marge d'une vente aux enchères sous le régime
/// de la marge (CGI art. 297 A I-2°) est, sur texte primaire (BOI-TVA-SECT-90-50 §270), la commission
/// totale du commissaire-priseur : <c>marge = Σ frais acheteur + Σ frais vendeur</c>, au grain LOT (no_ba).
/// Tous les montants sont en <see cref="decimal"/>, arrondi commercial half-up à 2 décimales
/// (<see cref="Liakont.Agent.Contracts.PivotRounding.RoundAmount"/>, CLAUDE.md n°1). C'est une BASE :
/// aucune TVA n'y figure distinctement (art. 297 E) — la garde 297 E vit dans
/// <see cref="MarginCalculator"/>.
/// </summary>
public sealed record LotMargin
{
    /// <summary>Identifiant du lot (no_ba EncheresV6) — clé de regroupement des frais.</summary>
    public required string LotReference { get; init; }

    /// <summary>Σ des frais acheteur du lot (B2C-08c), decimal half-up. 2e jambe de la marge.</summary>
    public required decimal BuyerFeesTotal { get; init; }

    /// <summary>Σ des frais vendeur du lot (B2C-08), decimal half-up. 1re jambe de la marge.</summary>
    public required decimal SellerFeesTotal { get; init; }

    /// <summary>Montant de marge du lot : <c>BuyerFeesTotal + SellerFeesTotal</c>, decimal half-up. BASE, sans TVA distincte.</summary>
    public required decimal MarginAmount { get; init; }
}
