namespace Liakont.Agent.Adapters.EncheresV6.Source;

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Reflet BRUT d'un bordereau ACHETEUR (table <c>entete_ba</c> du système EncheresV6) avec ses lignes
/// (<c>lignes_ba</c>) jointes. Un bordereau de vente porte <c>bordereau_ou_avoir = "B"</c> ; un avoir
/// porte <c>"A"</c> et référence sa facture d'origine via <c>no_ba_lettrage</c>. L'adaptateur ne classe
/// PAS facture/avoir et n'interprète PAS les champs : il transporte <c>bordereau_ou_avoir</c> en
/// <c>SourceDocumentKind</c> brut (ADR-0004 D3-3), la classification vit dans Validation.
/// <para>
/// Le code régime de TVA est porté par <c>ligne_pv</c> (pas par <c>lignes_ba</c>) : il est rapporté sur
/// chaque ligne par la jointure <c>ligne_pv</c> sur <c>(no_ba, no_ligne_pv)</c>. Les montants sont en
/// <see cref="double"/> à dessein (flottants legacy Pervasive) ; la conversion en <c>decimal</c> au
/// centime half-up est faite à la frontière par <see cref="EncheresV6RowMapper"/> (CLAUDE.md n°1).
/// </para>
/// </summary>
internal sealed class EncheresV6Bordereau
{
    /// <summary>Identifiant interne du bordereau (<c>no_ba</c>) — Number (BT-1) et base de la référence source.</summary>
    [JsonProperty("no_ba")]
    public string? NoBa { get; set; }

    /// <summary>Type de pièce source, BRUT : « B » bordereau de vente, « A » avoir (jamais interprété ici).</summary>
    [JsonProperty("bordereau_ou_avoir")]
    public string? BordereauOuAvoir { get; set; }

    /// <summary>Date de vente / d'émission (EN 16931 BT-2).</summary>
    [JsonProperty("date_vente")]
    public DateTime DateVente { get; set; }

    /// <summary>Pour un avoir : <c>no_ba</c> du bordereau d'origine lettré (lien avoir → facture).</summary>
    [JsonProperty("no_ba_lettrage")]
    public string? NoBaLettrage { get; set; }

    /// <summary>Nom de l'acheteur (destinataire), tel que stocké dans la source.</summary>
    [JsonProperty("nom")]
    public string? Nom { get; set; }

    /// <summary>Prénom de l'acheteur (composé avec le nom pour le tiers).</summary>
    [JsonProperty("prenom")]
    public string? Prenom { get; set; }

    /// <summary>Champ source <c>societe</c> de l'acheteur — non vide ⇒ <c>IsCompanyHint</c> brut (aucune heuristique ici, VAL05).</summary>
    [JsonProperty("societe")]
    public string? Societe { get; set; }

    /// <summary>SIREN de l'acheteur (colonne <c>acheteur_siren</c>, paramétrage déploiement) — transporté brut, jamais déduit. <c>null</c> si absent.</summary>
    [JsonProperty("acheteur_siren")]
    public string? AcheteurSiren { get; set; }

    /// <summary>N° TVA intracommunautaire de l'acheteur (<c>tva_cee</c>), transporté brut (EN 16931 BT-31).</summary>
    [JsonProperty("tva_cee")]
    public string? TvaCee { get; set; }

    /// <summary>Adresse de l'acheteur (ligne 1).</summary>
    [JsonProperty("adresse")]
    public string? Adresse { get; set; }

    /// <summary>Code postal de l'acheteur.</summary>
    [JsonProperty("code_postal")]
    public string? CodePostal { get; set; }

    /// <summary>Ville de l'acheteur.</summary>
    [JsonProperty("ville")]
    public string? Ville { get; set; }

    /// <summary>Code pays ISO 3166-1 alpha-2 de l'acheteur (EN 16931 BT-55). Absent = <c>null</c>.</summary>
    [JsonProperty("code_pays")]
    public string? CodePays { get; set; }

    /// <summary>Total TTC d'entête stocké par la source (<c>total_bordereau</c>) — porté en contrôle (SourceTotalGross).</summary>
    [JsonProperty("total_bordereau")]
    public double TotalBordereau { get; set; }

    /// <summary>Devise du bordereau (par défaut EUR si non renseignée par les lignes).</summary>
    [JsonProperty("code_devise")]
    public string? CodeDevise { get; set; }

    /// <summary>Lignes du bordereau (jointure <c>lignes_ba</c> + <c>ligne_pv</c>) : lots (type 1), frais (type 2), règlements (type 3).</summary>
    [JsonProperty("lignes")]
    public List<EncheresV6Ligne> Lignes { get; } = new List<EncheresV6Ligne>();
}
