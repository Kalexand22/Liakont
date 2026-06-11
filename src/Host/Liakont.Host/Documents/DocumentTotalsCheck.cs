namespace Liakont.Host.Documents;

/// <summary>
/// Résultat du contrôle de cohérence totaux ↔ lignes affiché dans l'onglet « Contenu » (FIX205, scénario de
/// recette S2.5). MIROIR EXACT de la règle de validation <c>LineTotalsRule</c> (F04 §3.3, BR-CO-13), pour ne
/// PAS produire un faux écart là où la validation, elle, n'en voit pas :
/// <list type="bullet">
///   <item>Net (BT-109) = Σ lignes HT (BT-131) − remises (BG-20) + charges (BG-21), arrondi half-up 2 déc.,
///   tolérance 0 — les charges/remises de niveau document SONT intégrées (sinon faux écart sur tout document
///   en portant).</item>
///   <item>TVA (BT-110) : réconciliée UNIQUEMENT quand le document ne porte aucune charge/remise globale (la
///   TVA de ces charges n'est pas résolue à ce stade — même limite connue que <c>LineTotalsRule</c>).</item>
/// </list>
/// Le contrôle SIGNALE l'écart, il ne corrige RIEN (aucun montant recalculé — CLAUDE.md n°1/3). Affichage seul.
/// </summary>
public sealed record DocumentTotalsCheck
{
    /// <summary>HT attendu = arrondi(Σ lignes + charges/remises document) — la valeur comparée au total (BR-CO-13).</summary>
    public required decimal ExpectedNet { get; init; }

    /// <summary>Total HT du document (BT-109), arrondi half-up 2 décimales.</summary>
    public required decimal DocumentNet { get; init; }

    /// <summary><c>true</c> si <see cref="ExpectedNet"/> = <see cref="DocumentNet"/> (tolérance 0).</summary>
    public required bool NetConsistent { get; init; }

    /// <summary><c>true</c> si la TVA a été réconciliée (document sans charge/remise globale), sinon non contrôlée.</summary>
    public required bool TaxChecked { get; init; }

    /// <summary>Somme de la TVA des lignes, arrondie (pertinente seulement si <see cref="TaxChecked"/>).</summary>
    public required decimal LinesTax { get; init; }

    /// <summary>Total TVA du document (BT-110), arrondi.</summary>
    public required decimal DocumentTax { get; init; }

    /// <summary><c>true</c> si la TVA concorde, ou si elle n'a pas été contrôlée (aucune objection).</summary>
    public required bool TaxConsistent { get; init; }

    /// <summary>Cohérence globale : net cohérent ET (TVA non contrôlée OU TVA cohérente).</summary>
    public bool Consistent => NetConsistent && (!TaxChecked || TaxConsistent);
}
