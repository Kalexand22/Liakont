namespace Liakont.Agent.Adapters.EncheresV6.Source;

using Newtonsoft.Json;

/// <summary>
/// Reflet BRUT d'une ligne de la table <c>lignes_bv</c> (bordereau vendeur) du système EncheresV6. Le
/// <c>type_ligne</c> est SOURCÉ par le dictionnaire Magic XPA (Range « 1=lot, 2=frais, 3=debours,
/// 4=paiement ») : <b>type 1</b> = lot (adjudication) ; <b>type 2</b> = commission vendeur
/// (<c>mtt_frais_ht</c>/<c>mtt_tva_frais</c>) = jambe vendeur de la marge ; <b>type 3</b> = débours
/// (transport, magasinage, expert, droit de suite… — HORS marge, « 3e terme » BOI §270) ; <b>type 4</b>
/// = paiement. Montants en <see cref="double"/> (flottants legacy) — conversion <c>decimal</c> half-up
/// à la frontière par le mapper (CLAUDE.md n°1).
/// </summary>
internal sealed class EncheresV6LigneVendeur
{
    /// <summary>Type de ligne source, BRUT : « 1 » lot, « 2 » commission vendeur, « 3 » débours, « 4 » paiement.</summary>
    [JsonProperty("type_ligne")]
    public string? TypeLigne { get; set; }

    /// <summary>Code de ligne (catégorie interne).</summary>
    [JsonProperty("code_ligne")]
    public string? CodeLigne { get; set; }

    /// <summary>Référence de la ligne de PV (lot) — traçabilité.</summary>
    [JsonProperty("no_ligne_pv")]
    public string? NoLignePv { get; set; }

    /// <summary>Libellé de la ligne (<c>libelle_ligne</c>) → Description.</summary>
    [JsonProperty("libelle_ligne")]
    public string? Designation { get; set; }

    /// <summary>Montant HT de l'adjudication du lot (<c>montant_adj_ht</c>), brut (flottant source).</summary>
    [JsonProperty("montant_adj_ht")]
    public double MontantAdjHt { get; set; }

    /// <summary>Commission vendeur HT (<c>mtt_frais_ht</c>), brut — jambe vendeur de la marge (avec la TVA frais → TTC).</summary>
    [JsonProperty("mtt_frais_ht")]
    public double MttFraisHt { get; set; }

    /// <summary>TVA de la commission vendeur (<c>mtt_tva_frais</c>), brut — composante TTC du frais vendeur.</summary>
    [JsonProperty("mtt_tva_frais")]
    public double MttTvaFrais { get; set; }

    /// <summary>Devise de la ligne (<c>code_devise</c>). Absent = devise domestique.</summary>
    [JsonProperty("code_devise")]
    public string? CodeDevise { get; set; }
}
