namespace Liakont.Agent.Adapters.EncheresV6.Source;

using System;
using Newtonsoft.Json;

/// <summary>
/// Reflet BRUT d'une ligne de la table <c>lignes_ba</c> (bordereau acheteur) du système EncheresV6,
/// enrichie du code régime TVA du lot (jointure <c>ligne_pv</c> sur <c>no_ligne_tout_pv</c>). Le
/// <c>type_ligne</c> distingue (vérifié sur la donnée) : <b>type 1</b> = ligne de lot (adjudication
/// <c>montant_adj_ht</c> + commission acheteur <c>montant_frais_ht</c>/<c>montant_tva_frais</c>) ;
/// <b>type 2</b> = débours/annexes acheteur (hors marge) ; <b>type 3</b> = règlement (<c>montant_ligne</c>) ;
/// <b>type 4</b> = RÉCAP (mêmes totaux que type 1, ignoré). Montants en <see cref="double"/> à dessein
/// (flottants legacy Pervasive) — conversion en <c>decimal</c> half-up à la frontière par le mapper
/// (CLAUDE.md n°1), original conservé dans <c>SourceData</c>.
/// </summary>
internal sealed class EncheresV6Ligne
{
    /// <summary>Type de ligne source, BRUT : « 1 » lot, « 2 » débours acheteur, « 3 » règlement, « 4 » récap.</summary>
    [JsonProperty("type_ligne")]
    public string? TypeLigne { get; set; }

    /// <summary>Code de ligne (catégorie/régime interne ou code de règlement CB/CE/EE pour le type 3).</summary>
    [JsonProperty("code_ligne")]
    public string? CodeLigne { get; set; }

    /// <summary>Référence de la ligne de PV (lot), relative au PV — traçabilité (n'est PAS la clé de jointure du régime).</summary>
    [JsonProperty("no_ligne_pv")]
    public string? NoLignePv { get; set; }

    /// <summary>Identifiant GLOBAL de ligne de PV (<c>no_ligne_tout_pv</c>) — VRAIE clé de jointure vers <c>ligne_pv</c> pour le code régime du lot (<c>ligne_pv.no_ba</c> vaut souvent 0).</summary>
    [JsonProperty("no_ligne_tout_pv")]
    public string? NoLigneToutPv { get; set; }

    /// <summary>Libellé de la ligne (<c>libelle_ligne</c>) → <c>PivotLineDto.Description</c> / fee Description.</summary>
    [JsonProperty("libelle_ligne")]
    public string? Designation { get; set; }

    /// <summary>Montant HT de l'adjudication du lot (<c>montant_adj_ht</c>), brut (flottant source).</summary>
    [JsonProperty("montant_adj_ht")]
    public double MontantAdjHt { get; set; }

    /// <summary>TVA d'adjudication « incluse » (<c>mtt_tva_inclus_adj</c>), brut. Sommée avec « en plus » pour la TVA totale d'adjudication.</summary>
    [JsonProperty("mtt_tva_inclus_adj")]
    public double MttTvaInclusAdj { get; set; }

    /// <summary>TVA d'adjudication « en sus » (<c>mtt_tva_en_plus_adj</c>), brut. Sommée avec « incluse » pour la TVA totale d'adjudication.</summary>
    [JsonProperty("mtt_tva_en_plus_adj")]
    public double MttTvaEnPlusAdj { get; set; }

    /// <summary>Commission acheteur HT (<c>montant_frais_ht</c>), brut — jambe acheteur de la marge (avec la TVA frais → TTC).</summary>
    [JsonProperty("montant_frais_ht")]
    public double MontantFraisHt { get; set; }

    /// <summary>TVA de la commission acheteur (<c>montant_tva_frais</c>), brut — composante TTC du frais acheteur.</summary>
    [JsonProperty("montant_tva_frais")]
    public double MontantTvaFrais { get; set; }

    /// <summary>Montant d'un règlement (<c>montant_ligne</c>, lignes type 3 uniquement), brut.</summary>
    [JsonProperty("montant_ligne")]
    public double MontantLigne { get; set; }

    /// <summary>Code régime TVA du lot, BRUT — rapporté par la jointure <c>ligne_pv.code_regime_tva</c> (R3, jamais interprété).</summary>
    [JsonProperty("code_regime")]
    public string? CodeRegime { get; set; }

    /// <summary>Devise de la ligne (<c>code_devise</c>). Absent = devise domestique.</summary>
    [JsonProperty("code_devise")]
    public string? CodeDevise { get; set; }

    /// <summary>Date de règlement (lignes type 3 uniquement).</summary>
    [JsonProperty("date_reglement")]
    public DateTime? DateReglement { get; set; }

    /// <summary>Numéro de remise du règlement (lignes type 3), référence source de l'encaissement.</summary>
    [JsonProperty("no_remise")]
    public string? NoRemise { get; set; }
}
