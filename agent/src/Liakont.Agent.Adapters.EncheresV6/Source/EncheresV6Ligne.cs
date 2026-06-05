namespace Liakont.Agent.Adapters.EncheresV6.Source;

using System;
using Newtonsoft.Json;

/// <summary>
/// Reflet BRUT d'une ligne de la table <c>lignes_ba</c> du système EncheresV6 (F01-F02 §4.3),
/// telle que renvoyée par la jointure <c>lignes_ba</c> × <c>Regime_tva</c>. Le type de ligne
/// distingue les lignes d'un document (type 4 = adjudication, type 2 = frais) des règlements
/// (type 3 = encaissement). Les montants sont en <see cref="double"/> à dessein : les bases
/// legacy (Pervasive) stockent des flottants « sales » — la conversion en <c>decimal</c> arrondie
/// au centime (half-up) est faite à la frontière par <see cref="EncheresV6RowMapper"/>
/// (ADR-0004 D3-7, CLAUDE.md n°1), l'original étant conservé dans <c>SourceData</c>.
/// </summary>
internal sealed class EncheresV6Ligne
{
    /// <summary>Type de ligne source, BRUT : « 4 » adjudication, « 2 » frais, « 3 » règlement (F01-F02 §4.3).</summary>
    [JsonProperty("type_ligne")]
    public string? TypeLigne { get; set; }

    /// <summary>Libellé de la ligne (devient <c>PivotLineDto.Description</c>).</summary>
    [JsonProperty("designation")]
    public string? Designation { get; set; }

    /// <summary>Montant HT de la ligne, brut (flottant source) — converti en decimal half-up par le mapper.</summary>
    [JsonProperty("montant_ht")]
    public double MontantHt { get; set; }

    /// <summary>Montant de TVA de la ligne, brut (flottant source) — calculé par la source, jamais par l'adaptateur (R3).</summary>
    [JsonProperty("montant_tva")]
    public double MontantTva { get; set; }

    /// <summary>Taux de TVA affiché sur la ligne (%), tel que rendu par la source. Absent = <c>null</c>.</summary>
    [JsonProperty("taux_tva")]
    public double? TauxTva { get; set; }

    /// <summary>Quantité de la ligne (défaut 1 si absente).</summary>
    [JsonProperty("quantite")]
    public double? Quantite { get; set; }

    /// <summary>Prix unitaire HT, brut (flottant source). Absent = <c>null</c>.</summary>
    [JsonProperty("prix_unitaire")]
    public double? PrixUnitaire { get; set; }

    /// <summary>Code régime TVA source de la ligne, BRUT (jointure <c>Regime_tva</c>) — transporté tel quel.</summary>
    [JsonProperty("code_regime")]
    public string? CodeRegime { get; set; }

    /// <summary>Référence de la ligne dans le système source (traçabilité).</summary>
    [JsonProperty("no_ligne")]
    public string? NoLigne { get; set; }

    /// <summary>Date de règlement (lignes type 3 uniquement).</summary>
    [JsonProperty("date_reglement")]
    public DateTime? DateReglement { get; set; }

    /// <summary>Mode de règlement (lignes type 3) : CB, chèque, espèces, virement (informatif).</summary>
    [JsonProperty("mode_reglement")]
    public string? ModeReglement { get; set; }

    /// <summary>Numéro de remise du règlement (lignes type 3), référence source de l'encaissement.</summary>
    [JsonProperty("no_remise")]
    public string? NoRemise { get; set; }
}
