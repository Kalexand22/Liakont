namespace Liakont.Modules.TvaMapping.Domain.Services;

/// <summary>
/// Liste FERMÉE des codes VATEX (motif d'exonération) proposés à l'édition de la table de mapping TVA
/// (item TVA05 / WEB07b). Chaque entrée est TRANSCRITE telle quelle du tableau « Codes VATEX clés »
/// de <c>docs/conception/F03-Mapping-TVA.md §2.2</c> — AUCUNE valeur n'est inventée (CLAUDE.md n°2) :
/// la console n'offre que des codes sourcés, jamais une saisie libre.
/// <para>
/// ⚠️ Même statut « à confirmer » que les catégories (<see cref="VatCategoryParser"/> / F03 §2.1) :
/// la liste provient des sources Peppol/EC officielles, cohérente avec le staging B2Brouter, mais
/// reste à re-vérifier sur le code list officiel EN 16931 v17.0 + EXTENDED-CTC-FR avant figeage du
/// mapping de production (F03 §6, décisions 2 et 4). Ce catalogue ne fait QUE proposer des codes à
/// l'opérateur : la validation structurelle (VATEX obligatoire si catégorie E) reste du ressort de
/// <see cref="MappingTableValidator"/>, qui n'est pas modifié par cette liste.
/// </para>
/// </summary>
public static class VatexCatalog
{
    /// <summary>
    /// Codes VATEX admis, dans l'ordre du tableau F03 §2.2 (code + usage transcrit). La description
    /// est le libellé « Usage » de la spec, pour l'affichage console — pas une règle fiscale dérivée.
    /// </summary>
    public static readonly IReadOnlyList<VatexCatalogEntry> All = new[]
    {
        new VatexCatalogEntry("VATEX-EU-F", "Biens d'occasion (régime de la marge)"),
        new VatexCatalogEntry("VATEX-EU-I", "Œuvres d'art (régime de la marge)"),
        new VatexCatalogEntry("VATEX-EU-J", "Objets de collection et d'antiquité (régime de la marge)"),
        new VatexCatalogEntry("VATEX-EU-AE", "Autoliquidation"),
        new VatexCatalogEntry("VATEX-EU-IC", "Livraison intra-UE"),
        new VatexCatalogEntry("VATEX-EU-G", "Export hors UE"),
        new VatexCatalogEntry("VATEX-EU-O", "Non soumis à la TVA"),
        new VatexCatalogEntry("VATEX-FR-FRANCHISE", "Franchise en base (art. 293 B)"),
        new VatexCatalogEntry("VATEX-FR-AE", "Autoliquidation art. 283-2 CGI (domestique)"),
        new VatexCatalogEntry("VATEX-FR-CNWVAT", "Avoirs sans TVA"),
        new VatexCatalogEntry("VATEX-FR-298SEXDECIESA", "Agences de voyages (art. 298 sexdecies A)"),
    };

    /// <summary>Codes VATEX admis seuls (dérivés de <see cref="All"/> — source unique de vérité).</summary>
    public static readonly IReadOnlyList<string> AllowedCodes =
        All.Select(entry => entry.Code).ToArray();
}
