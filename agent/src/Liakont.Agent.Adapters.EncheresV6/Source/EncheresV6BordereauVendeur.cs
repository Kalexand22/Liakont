namespace Liakont.Agent.Adapters.EncheresV6.Source;

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Reflet BRUT d'un bordereau VENDEUR (table <c>entete_bv</c> du système EncheresV6) avec ses lignes
/// (<c>lignes_bv</c>) jointes. Porte la JAMBE VENDEUR de la marge e-reporting B2C : la commission
/// vendeur (lignes <c>type_ligne = "2"</c>) est la donnée de calcul de marge, agrégée avec la
/// commission acheteur du BA par la plateforme (un seul report SE, F03 §2.5). Le destinataire est le
/// VENDEUR (commettant) ; sous le régime de la marge il est non assujetti (particulier). Montants en
/// <see cref="double"/> (flottants legacy) — conversion <c>decimal</c> half-up à la frontière (CLAUDE.md n°1).
/// </summary>
internal sealed class EncheresV6BordereauVendeur
{
    /// <summary>Identifiant interne du bordereau vendeur (<c>no_bv</c>) — Number (BT-1) et base de la référence source.</summary>
    [JsonProperty("no_bv")]
    public string? NoBv { get; set; }

    /// <summary>Type de pièce source, BRUT : « B » bordereau, « A » avoir (jamais interprété ici).</summary>
    [JsonProperty("bordereau_ou_avoir")]
    public string? BordereauOuAvoir { get; set; }

    /// <summary>Date de vente / d'émission (EN 16931 BT-2).</summary>
    [JsonProperty("date_vente")]
    public DateTime DateVente { get; set; }

    /// <summary>Pour un avoir : <c>no_bv</c> du bordereau d'origine lettré (<c>no_bv_lettrage</c>).</summary>
    [JsonProperty("no_bv_lettrage")]
    public string? NoBvLettrage { get; set; }

    /// <summary>Nom du vendeur (commettant), tel que stocké dans la source.</summary>
    [JsonProperty("nom")]
    public string? Nom { get; set; }

    /// <summary>Prénom du vendeur (composé avec le nom pour le tiers).</summary>
    [JsonProperty("prenom")]
    public string? Prenom { get; set; }

    /// <summary>Code postal du vendeur.</summary>
    [JsonProperty("code_postal")]
    public string? CodePostal { get; set; }

    /// <summary>Ville du vendeur.</summary>
    [JsonProperty("ville")]
    public string? Ville { get; set; }

    /// <summary>Code pays ISO 3166-1 alpha-2 du vendeur (EN 16931 BT-55). Absent = <c>null</c>.</summary>
    [JsonProperty("code_pays")]
    public string? CodePays { get; set; }

    /// <summary>Code régime TVA du bordereau vendeur (<c>code_regime_tva</c>), BRUT — porté sur les frais vendeur.</summary>
    [JsonProperty("code_regime_tva")]
    public string? CodeRegimeTva { get; set; }

    /// <summary>Total TTC d'entête stocké par la source (<c>total_bordereau</c>) — porté en contrôle (SourceTotalGross).</summary>
    [JsonProperty("total_bordereau")]
    public double TotalBordereau { get; set; }

    /// <summary>Devise du bordereau (par défaut EUR si non renseignée par les lignes).</summary>
    [JsonProperty("code_devise")]
    public string? CodeDevise { get; set; }

    /// <summary>Lignes du bordereau vendeur (jointure <c>lignes_bv</c>) : lots (type 1), commission (type 2), débours (type 3), paiement (type 4).</summary>
    [JsonProperty("lignes")]
    public List<EncheresV6LigneVendeur> Lignes { get; } = new List<EncheresV6LigneVendeur>();
}
