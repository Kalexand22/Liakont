namespace Liakont.Agent.Adapters.EncheresV6.Source;

using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Instantané BRUT d'une source EncheresV6 rejouable par le <see cref="EncheresV6FixtureExtractor"/> :
/// le contenu d'un fichier de fixtures JSON (régimes + bordereaux avec leurs lignes). Modélise ce
/// que le futur PervasiveExtractor (ADP02) obtiendra par requêtes ODBC en lecture seule — la
/// transformation vers le pivot (<see cref="EncheresV6RowMapper"/>) est strictement la même, seule
/// la source des données diffère (F01-F02 §4.4).
/// </summary>
internal sealed class EncheresV6SourceSnapshot
{
    /// <summary>Régimes de TVA déclarés par la source (table <c>Regime_tva</c>).</summary>
    [JsonProperty("regimes")]
    public List<EncheresV6Regime> Regimes { get; } = new List<EncheresV6Regime>();

    /// <summary>Bordereaux ACHETEUR (ventes et avoirs) avec leurs lignes (tables <c>entete_ba</c> + <c>lignes_ba</c>).</summary>
    [JsonProperty("bordereaux")]
    public List<EncheresV6Bordereau> Bordereaux { get; } = new List<EncheresV6Bordereau>();

    /// <summary>Bordereaux VENDEUR (jambe vendeur de la marge) avec leurs lignes (tables <c>entete_bv</c> + <c>lignes_bv</c>).</summary>
    [JsonProperty("bordereaux_vendeur")]
    public List<EncheresV6BordereauVendeur> BordereauxVendeur { get; } = new List<EncheresV6BordereauVendeur>();

    /// <summary>Factures clients ORDINAIRES (hors enchères) avec leurs lignes (tables <c>entete_facture_clien</c> + <c>ligne_facture_client</c>).</summary>
    [JsonProperty("factures_clients")]
    public List<EncheresV6FactureClient> FacturesClients { get; } = new List<EncheresV6FactureClient>();
}
