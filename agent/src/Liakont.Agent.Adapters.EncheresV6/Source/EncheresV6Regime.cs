namespace Liakont.Agent.Adapters.EncheresV6.Source;

using Newtonsoft.Json;

/// <summary>
/// Reflet BRUT d'une ligne de la table <c>Regime_tva</c> du système EncheresV6 (F01-F02 §4.3).
/// L'adaptateur ne mappe ni n'interprète le régime (R3) : il transporte le code source tel quel
/// (vers <c>SourceRegimeCodes</c>) et expose le libellé/les occurrences pour le paramétrage de la
/// table de mapping plateforme (F03) et la détection des régimes non couverts (TVA03). Modèle
/// partagé entre le <see cref="EncheresV6FixtureExtractor"/> (mode fixtures) et le futur
/// PervasiveExtractor (ODBC réel, ADP02) — seule la source des lignes diffère.
/// </summary>
internal sealed class EncheresV6Regime
{
    /// <summary>Code régime TVA source, BRUT (ex. « 5 » assujetti normal, « 6 » régime de la marge).</summary>
    [JsonProperty("code_regime")]
    public string? CodeRegime { get; set; }

    /// <summary>Libellé du régime tel que stocké dans la base source.</summary>
    [JsonProperty("libelle")]
    public string? Libelle { get; set; }

    /// <summary>Taux de TVA déclaré du régime (informatif). L'adaptateur ne calcule rien à partir de lui.</summary>
    [JsonProperty("taux")]
    public double? Taux { get; set; }

    /// <summary>Indicateur source « assujetti à la TVA ».</summary>
    [JsonProperty("assujetti_tva")]
    public bool AssujettiTva { get; set; }

    /// <summary>Indicateur source « régime de la marge ».</summary>
    [JsonProperty("regime_marge")]
    public bool RegimeMarge { get; set; }
}
