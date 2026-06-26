namespace Liakont.Agent.Adapters.EncheresV6.Source;

using Newtonsoft.Json;

/// <summary>
/// Reflet BRUT d'une ligne de la table <c>ligne_facture_client</c> (facture client) du système EncheresV6.
/// Le <c>type_ligne</c> distingue (vérifié sur la donnée) : <b>type 1</b> = ligne FACTURÉE (article /
/// prestation : <c>qte</c> × <c>prix_unitaire_ht</c> au taux <c>taux_tva</c>, régime <c>code_tva</c>) ; les
/// lignes de pur commentaire (TXT, <c>qte=0</c> et <c>prix_unitaire_ht=0</c>) sont écartées par le mapper ;
/// <b>type 2</b> = règlement (encaissement, exclu — l'e-reporting porte sur la facture, pas son règlement).
/// <para>
/// Il n'y a PAS de <c>montant_tva</c> par ligne : la TVA de la ligne se calcule comme la source elle-même le
/// fait (HT × <c>taux_tva</c>), arithmétique de transport au centime half-up par le mapper (CLAUDE.md n°1).
/// La clé de régime est le <c>taux_tva</c> (formaté brut), PAS le <c>code_tva</c> : ce dernier est NON FIABLE
/// dans la donnée réelle (il diverge du taux appliqué — p. ex. <c>code_tva=0</c> avec un taux 20 %) ; il est
/// transporté en SourceData pour l'audit. La plateforme mappe le taux vers une catégorie par la table validée (R3).
/// </para>
/// </summary>
internal sealed class EncheresV6FactureClientLigne
{
    /// <summary>Type de ligne source, BRUT : « 1 » ligne facturée, « 2 » règlement (jamais interprété ici).</summary>
    [JsonProperty("type_ligne")]
    public string? TypeLigne { get; set; }

    /// <summary>Numéro d'ordre de la ligne dans la facture (<c>no_ligne</c>) — traçabilité (référence de ligne source).</summary>
    [JsonProperty("no_ligne")]
    public string? NoLigne { get; set; }

    /// <summary>Code article source (<c>code_article</c>) — p. ex. HONO, DIV, CV, TVA0, TXT (commentaire). Transporté brut.</summary>
    [JsonProperty("code_article")]
    public string? CodeArticle { get; set; }

    /// <summary>Désignation de la ligne (<c>designation</c>) → <c>PivotLineDto.Description</c>.</summary>
    [JsonProperty("designation")]
    public string? Designation { get; set; }

    /// <summary>Quantité facturée (<c>qte</c>), brute. 0 sur une ligne de commentaire ou de règlement.</summary>
    [JsonProperty("qte")]
    public int Qte { get; set; }

    /// <summary>Prix unitaire HT (<c>prix_unitaire_ht</c>), brut (flottant legacy). Base de la ligne (HT = qte × prix).</summary>
    [JsonProperty("prix_unitaire_ht")]
    public double PrixUnitaireHt { get; set; }

    /// <summary>Code régime de TVA de la ligne (<c>code_tva</c>, smallint), BRUT — clé de régime mappée par la plateforme.</summary>
    [JsonProperty("code_tva")]
    public int CodeTva { get; set; }

    /// <summary>Taux de TVA de la ligne (<c>taux_tva</c>, p. ex. 20 / 5.5 / 0), brut — base du calcul de TVA de la ligne (HT × taux).</summary>
    [JsonProperty("taux_tva")]
    public double TauxTva { get; set; }
}
