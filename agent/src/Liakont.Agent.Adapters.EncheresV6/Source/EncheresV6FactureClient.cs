namespace Liakont.Agent.Adapters.EncheresV6.Source;

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Reflet BRUT d'une FACTURE CLIENT (table <c>entete_facture_clien</c> du système EncheresV6) avec ses
/// lignes (<c>ligne_facture_client</c>) jointes. Document ORDINAIRE émis DIRECTEMENT par l'OVV, HORS
/// mécanisme d'enchères opaque (pas de bordereau, pas de frais d'enchères) : une facture porte
/// <c>facture_ou_avoir = "F"</c> ; un avoir porte <c>"A"</c> et référence sa facture d'origine via
/// <c>no_facture_lettrage</c>. L'adaptateur ne classe PAS facture/avoir (transport brut de
/// <c>facture_ou_avoir</c> en <c>SourceDocumentKind</c>, ADR-0004 D3-3) et n'interprète AUCUN champ fiscal.
/// <para>
/// Le régime de TVA est porté PAR LIGNE par <c>ligne_facture_client.code_tva</c> (smallint, transporté brut
/// en clé de régime) — il n'y a PAS de table <c>ligne_pv</c> ici (document hors enchères). Les montants HT/TVA
/// d'entête sont en <see cref="double"/> (flottants legacy) ; la conversion <c>decimal</c> half-up au centime
/// est faite à la frontière par <see cref="EncheresV6RowMapper"/> (CLAUDE.md n°1). Filtre tenant par
/// <c>dossier_cpt</c> (smallint).
/// </para>
/// </summary>
internal sealed class EncheresV6FactureClient
{
    /// <summary>Numéro de la facture (<c>no_fact</c>) — Number (BT-1) et base de la référence source.</summary>
    [JsonProperty("no_fact")]
    public string? NoFact { get; set; }

    /// <summary>Type de pièce source, BRUT : « F » facture, « A » avoir (jamais interprété ici).</summary>
    [JsonProperty("facture_ou_avoir")]
    public string? FactureOuAvoir { get; set; }

    /// <summary>Date de facture / d'émission (EN 16931 BT-2).</summary>
    [JsonProperty("date_fact")]
    public DateTime DateFact { get; set; }

    /// <summary>Pour un avoir : <c>no_fact</c> de la facture d'origine lettrée (lien avoir → facture).</summary>
    [JsonProperty("no_facture_lettrage")]
    public string? NoFactureLettrage { get; set; }

    /// <summary>Nom du client (destinataire), tel que stocké dans la source.</summary>
    [JsonProperty("nom")]
    public string? Nom { get; set; }

    /// <summary>Prénom du client (composé avec le nom pour le tiers).</summary>
    [JsonProperty("prenom")]
    public string? Prenom { get; set; }

    /// <summary>Adresse du client (ligne 1, <c>adresse1</c>).</summary>
    [JsonProperty("adresse1")]
    public string? Adresse1 { get; set; }

    /// <summary>Code postal du client (<c>cp</c>).</summary>
    [JsonProperty("cp")]
    public string? Cp { get; set; }

    /// <summary>Ville du client.</summary>
    [JsonProperty("ville")]
    public string? Ville { get; set; }

    /// <summary>Code pays ISO 3166-1 alpha-2 du client (EN 16931 BT-55). Absent = <c>null</c>.</summary>
    [JsonProperty("code_pays")]
    public string? CodePays { get; set; }

    /// <summary>Total HT d'entête stocké par la source (<c>montant_ht</c>), brut — contrôle (jamais recalculé en source).</summary>
    [JsonProperty("montant_ht")]
    public double MontantHt { get; set; }

    /// <summary>Total TVA d'entête stocké par la source (<c>montant_tva</c>), brut — contrôle.</summary>
    [JsonProperty("montant_tva")]
    public double MontantTva { get; set; }

    /// <summary>Total TTC d'entête stocké par la source (<c>montant_ttc</c>) — porté en contrôle (SourceTotalGross).</summary>
    [JsonProperty("montant_ttc")]
    public double MontantTtc { get; set; }

    /// <summary>Devise de la facture (<c>code_devise</c>, par défaut EUR si non renseignée).</summary>
    [JsonProperty("code_devise")]
    public string? CodeDevise { get; set; }

    /// <summary>Lignes de la facture (<c>ligne_facture_client</c>) : facturées (type 1) et règlements (type 2).</summary>
    [JsonProperty("lignes")]
    public List<EncheresV6FactureClientLigne> Lignes { get; } = new List<EncheresV6FactureClientLigne>();
}
