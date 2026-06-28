namespace Liakont.Agent.Adapters.EncheresV6.Source;

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Reflet BRUT d'une NOTE D'HONORAIRES d'inventaire (table <c>entete_notes_hono</c> du système EncheresV6)
/// avec ses lignes (<c>lignes_notes_hono</c>) jointes. Document ORDINAIRE émis DIRECTEMENT par l'OVV pour
/// une PRESTATION DE SERVICES autonome (honoraires d'inventaire — souvent judiciaire, parfois volontaire),
/// HORS mécanisme d'enchères opaque (pas de bordereau, pas de frais d'enchères) : une note porte
/// <c>facture_ou_avoir = "F"</c> ; un avoir porte <c>"A"</c> et référence sa note d'origine via
/// <c>no_note_lettrage</c>. L'adaptateur ne classe PAS facture/avoir (transport brut, ADR-0004 D3-3).
/// <para>
/// La note ne porte PAS de <c>code_tva</c> (ni en entête ni par ligne) : le RÉGIME est recouvré par
/// l'agent comme le TAUX effectif de chaque ligne (<c>montant_tva</c> / <c>montant_ht</c>), transporté en
/// clé de régime — arithmétique sur deux champs source, la CATÉGORIE restant décidée par la table validée
/// de la plateforme (R3, arbitrage PO). L'entête ne porte que <c>montant_ttc</c> (HT/TVA viennent des
/// lignes). Filtre tenant par <c>No_dossier</c> (smallint). Montants en <see cref="double"/> (flottants
/// legacy) → conversion <c>decimal</c> half-up au centime par le mapper (CLAUDE.md n°1).
/// </para>
/// </summary>
internal sealed class EncheresV6NoteHono
{
    /// <summary>Numéro de la note (<c>no_note_hono</c>) — Number (BT-1) et base de la référence source.</summary>
    [JsonProperty("no_note_hono")]
    public string? NoNoteHono { get; set; }

    /// <summary>Type de pièce source, BRUT : « F » note, « A » avoir (jamais interprété ici).</summary>
    [JsonProperty("facture_ou_avoir")]
    public string? FactureOuAvoir { get; set; }

    /// <summary>Date de la note / d'émission (<c>date_facture</c>, EN 16931 BT-2).</summary>
    [JsonProperty("date_facture")]
    public DateTime DateFacture { get; set; }

    /// <summary>Pour un avoir : <c>no_note_hono</c> de la note d'origine lettrée (lien avoir → note).</summary>
    [JsonProperty("no_note_lettrage")]
    public string? NoNoteLettrage { get; set; }

    /// <summary>Nom du destinataire (commettant / mandant), tel que stocké dans la source.</summary>
    [JsonProperty("nom")]
    public string? Nom { get; set; }

    /// <summary>Prénom du destinataire (composé avec le nom pour le tiers).</summary>
    [JsonProperty("prenom")]
    public string? Prenom { get; set; }

    /// <summary>Adresse du destinataire (ligne 1, <c>adresse</c>).</summary>
    [JsonProperty("adresse")]
    public string? Adresse { get; set; }

    /// <summary>Code postal du destinataire (<c>code_postal</c>).</summary>
    [JsonProperty("code_postal")]
    public string? CodePostal { get; set; }

    /// <summary>Ville du destinataire.</summary>
    [JsonProperty("ville")]
    public string? Ville { get; set; }

    /// <summary>Code pays ISO 3166-1 alpha-2 du destinataire (EN 16931 BT-55). Absent = <c>null</c>.</summary>
    [JsonProperty("code_pays")]
    public string? CodePays { get; set; }

    /// <summary>Total TTC d'entête stocké par la source (<c>montant_ttc</c>) — porté en contrôle (SourceTotalGross).</summary>
    [JsonProperty("montant_ttc")]
    public double MontantTtc { get; set; }

    /// <summary>Devise de la note (<c>code_devise</c>, par défaut EUR si non renseignée).</summary>
    [JsonProperty("code_devise")]
    public string? CodeDevise { get; set; }

    /// <summary>Lignes de la note (<c>lignes_notes_hono</c>) : honoraires (type 1), frais (type 2), règlements (type 3).</summary>
    [JsonProperty("lignes")]
    public List<EncheresV6NoteHonoLigne> Lignes { get; } = new List<EncheresV6NoteHonoLigne>();
}
