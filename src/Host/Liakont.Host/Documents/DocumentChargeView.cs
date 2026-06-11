namespace Liakont.Host.Documents;

/// <summary>
/// Charge (EN 16931 BG-21) ou remise (BG-20) de niveau DOCUMENT, prête à afficher dans l'onglet « Contenu »
/// (FIX205). Distincte d'une ligne : elle s'applique au document entier (éco-contribution, remise globale…).
/// Affichée pour que le rapprochement totaux ↔ lignes soit COMPLET et lisible (sinon « Σ lignes ≠ Total HT »
/// sans explication). PROJECTION pure du pivot transmis — aucun calcul fiscal, le montant vient de la source.
/// </summary>
public sealed record DocumentChargeView
{
    /// <summary>Motif en clair (ex. « éco-contribution »), ou un libellé générique si la source n'en porte pas.</summary>
    public required string Label { get; init; }

    /// <summary><c>true</c> = charge (s'ajoute au HT, BG-21) ; <c>false</c> = remise (se retranche, BG-20).</summary>
    public required bool IsCharge { get; init; }

    /// <summary>Montant HT de la charge/remise (decimal, toujours positif — le signe est porté par <see cref="IsCharge"/>).</summary>
    public required decimal Amount { get; init; }
}
