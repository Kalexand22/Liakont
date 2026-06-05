namespace Liakont.Agent.Adapters.EncheresV6.Source;

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Reflet BRUT d'une ligne de la table <c>entete_ba</c> du système EncheresV6 (F01-F02 §4.3),
/// avec ses lignes (<c>lignes_ba</c>) jointes. Un bordereau de vente porte
/// <c>bordereau_ou_avoir = "B"</c> ; un avoir porte <c>"A"</c> et référence sa facture d'origine
/// via <c>no_ba_lettrage</c> (lien fiable — capacité <c>HasCreditNoteLink</c>). L'adaptateur ne
/// classe PAS facture/avoir et n'interprète PAS les champs : il transporte <c>bordereau_ou_avoir</c>
/// en <c>SourceDocumentKind</c> brut (ADR-0004 D3-3), la classification vit dans la Validation
/// plateforme.
/// </summary>
internal sealed class EncheresV6Bordereau
{
    /// <summary>Identifiant interne du bordereau dans la source (<c>no_ba</c>) — base de la référence source.</summary>
    [JsonProperty("no_ba")]
    public string? NoBa { get; set; }

    /// <summary>Numéro de pièce / document affiché (EN 16931 BT-1, clé d'idempotence vers la PA).</summary>
    [JsonProperty("numero_piece")]
    public string? NumeroPiece { get; set; }

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
    [JsonProperty("acheteur_nom")]
    public string? AcheteurNom { get; set; }

    /// <summary>Champ source <c>societe</c> de l'acheteur — non vide ⇒ <c>IsCompanyHint</c> brut (aucune heuristique ici, VAL05).</summary>
    [JsonProperty("acheteur_societe")]
    public string? AcheteurSociete { get; set; }

    /// <summary>SIREN de l'acheteur si présent en source (rare en B2C) — transporté brut, jamais déduit.</summary>
    [JsonProperty("acheteur_siren")]
    public string? AcheteurSiren { get; set; }

    /// <summary>Ville de l'acheteur.</summary>
    [JsonProperty("acheteur_ville")]
    public string? AcheteurVille { get; set; }

    /// <summary>Code postal de l'acheteur.</summary>
    [JsonProperty("acheteur_code_postal")]
    public string? AcheteurCodePostal { get; set; }

    /// <summary>Code pays ISO 3166-1 alpha-2 de l'acheteur (EN 16931 BT-55). Absent = <c>null</c>.</summary>
    [JsonProperty("acheteur_pays")]
    public string? AcheteurPays { get; set; }

    /// <summary>Total HT d'entête stocké par la source (devient <c>PivotTotalsDto.TotalNet</c>).</summary>
    [JsonProperty("total_ht")]
    public double TotalHt { get; set; }

    /// <summary>Total de TVA d'entête stocké par la source (devient <c>PivotTotalsDto.TotalTax</c>).</summary>
    [JsonProperty("total_tva")]
    public double TotalTva { get; set; }

    /// <summary>Total TTC d'entête stocké par la source (<c>total_bordereau</c>) — total et contrôle de cohérence.</summary>
    [JsonProperty("total_ttc")]
    public double TotalTtc { get; set; }

    /// <summary>Lignes du bordereau (jointure <c>lignes_ba</c>) : adjudication, frais et règlements.</summary>
    [JsonProperty("lignes")]
    public List<EncheresV6Ligne> Lignes { get; } = new List<EncheresV6Ligne>();
}
