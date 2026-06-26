namespace Liakont.Agent.Adapters.EncheresV6.Source;

using Newtonsoft.Json;

/// <summary>
/// Reflet BRUT d'une ligne de la table <c>lignes_notes_hono</c> (note d'honoraires) du système EncheresV6.
/// Le <c>type_ligne</c> distingue (vérifié sur la donnée) : <b>type 1</b> = honoraires d'inventaire ;
/// <b>type 2</b> = frais (déplacement, secrétariat, droits fixes, débours…) ; <b>type 3</b> = règlement
/// (encaissement, exclu — l'e-reporting porte sur la note, pas son règlement). Types 1 et 2 sont les lignes
/// FACTURÉES (leur somme = le TTC d'entête).
/// <para>
/// Chaque ligne porte explicitement <c>montant_ht</c> + <c>montant_tva</c> (+ <c>montant_ttc</c>) mais AUCUN
/// code de régime TVA : le mapper recouvre le TAUX effectif (<c>montant_tva</c> / <c>montant_ht</c>) comme
/// clé de régime (la plateforme tranche la catégorie via la table validée). Montants en <see cref="double"/>
/// (flottants legacy) → conversion <c>decimal</c> half-up au centime par le mapper (CLAUDE.md n°1).
/// </para>
/// </summary>
internal sealed class EncheresV6NoteHonoLigne
{
    /// <summary>Type de ligne source, BRUT : « 1 » honoraires, « 2 » frais, « 3 » règlement (jamais interprété ici).</summary>
    [JsonProperty("type_ligne")]
    public string? TypeLigne { get; set; }

    /// <summary>Code de ligne source (<c>code_ligne</c>) — type de frais (1 secrétariat, 2 déplacement, 7 droits fixes…), transporté brut.</summary>
    [JsonProperty("code_ligne")]
    public string? CodeLigne { get; set; }

    /// <summary>Libellé de la ligne (<c>libelle</c>) → <c>PivotLineDto.Description</c>.</summary>
    [JsonProperty("libelle")]
    public string? Libelle { get; set; }

    /// <summary>Montant HT de la ligne (<c>montant_ht</c>), brut (flottant legacy) — base de la ligne et du taux recouvré.</summary>
    [JsonProperty("montant_ht")]
    public double MontantHt { get; set; }

    /// <summary>Montant de TVA de la ligne (<c>montant_tva</c>), brut — TVA distincte de la ligne (et numérateur du taux recouvré).</summary>
    [JsonProperty("montant_tva")]
    public double MontantTva { get; set; }
}
