namespace Liakont.Host.Documents;

/// <summary>
/// Une ligne du document, prête à afficher dans l'onglet « Contenu » du détail (FIX205, F10 §2.3 :
/// « lignes : libellé, montants, régime TVA appliqué → catégorie/VATEX résultante »). PROJECTION en
/// lecture du pivot transmis (<see cref="DocumentLineProjection"/>) — aucune règle métier, aucun calcul
/// fiscal : les montants viennent du pivot (la source), la catégorie/le VATEX sont le RÉSULTAT du mapping
/// plateforme déjà tranché. Les libellés fiscaux (<see cref="Category"/>) sont déjà résolus en français
/// (transcrits de F03 §2.1) pour que la vue reste un rendu pur. Le « zéro JSON » de F10 §1 proscrit le dump
/// technique du pivot, pas cette restitution lisible.
/// </summary>
public sealed record DocumentLineView
{
    /// <summary>Libellé de la ligne (EN 16931 BT-153).</summary>
    public required string Label { get; init; }

    /// <summary>Quantité (EN 16931 BT-129).</summary>
    public required decimal Quantity { get; init; }

    /// <summary>Montant HT de la ligne (EN 16931 BT-131), decimal — affiché à 2 décimales par la vue.</summary>
    public required decimal NetAmount { get; init; }

    /// <summary>Régime(s) TVA de la source, BRUTS (codes joints), ou « — » si la ligne n'en porte aucun.</summary>
    public required string SourceRegime { get; init; }

    /// <summary>Catégorie TVA résultante, déjà rendue en français (« S — Taux normal »), ou « — » si non mappée.</summary>
    public required string Category { get; init; }

    /// <summary>Code VATEX d'exonération (EN 16931 BT-121), ou « — » si absent.</summary>
    public required string Vatex { get; init; }

    /// <summary>
    /// Mention EXPLICITE du régime de la marge (« Régime de la marge – objets de collection… ») quand la ligne est
    /// sous ce régime (catégorie E + VATEX-EU-F/I/J), sinon <c>null</c>. Présentation pure (dérivée du VATEX déjà
    /// mappé, <see cref="Components.MarginRegimeDisplay"/>) : évite que la ligne marge (TVA 0) soit prise pour une
    /// exonération classique. La mention légale 297 E figure en note sous le tableau du détail.
    /// </summary>
    public string? MarginMention { get; init; }

    /// <summary>Montant de TVA de la ligne (decimal), ou <c>null</c> si la ligne ne porte aucune ventilation.</summary>
    public required decimal? TaxAmount { get; init; }

    /// <summary>Taux de TVA en pourcentage (EN 16931 BT-152), ou <c>null</c> si inconnu / non uniforme.</summary>
    public required decimal? Rate { get; init; }
}
