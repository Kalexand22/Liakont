namespace Liakont.Agent.Adapters.DemoErpA.Source;

using System;
using System.Collections.Generic;

/// <summary>
/// Reflet BRUT d'une ligne de la table <c>factures</c> de la base de DÉMONSTRATION DemoErpA (ERP
/// normalisé fictif), avec ses lignes jointes. Le type de pièce « FAC » / « AVO » est transporté brut
/// (jamais classé ici — la classification facture/avoir vit dans la Validation plateforme). Montants
/// en <c>decimal</c> (le schéma A les stocke en <c>decimal(18,2)</c>). Données 100 % fictives (CLAUDE.md n°7).
/// </summary>
internal sealed class DemoErpAInvoice
{
    /// <summary>Identifiant interne de la facture (<c>facture_id</c>) — clé de regroupement des lignes.</summary>
    public string? FactureId { get; set; }

    /// <summary>Numéro de pièce affiché (EN 16931 BT-1).</summary>
    public string? Numero { get; set; }

    /// <summary>Type de pièce source BRUT : « FAC » ou « AVO ».</summary>
    public string? TypePiece { get; set; }

    /// <summary>Date d'émission (EN 16931 BT-2).</summary>
    public DateTime DateEmission { get; set; }

    /// <summary>Pour un avoir : numéro de la facture d'origine rectifiée (EN 16931 BT-25).</summary>
    public string? FactureOrigineNumero { get; set; }

    /// <summary>Pour un avoir : date d'émission de la facture d'origine (auto-jointure), sinon <c>null</c>.</summary>
    public DateTime? OrigineDate { get; set; }

    /// <summary>Devise ISO 4217 (défaut EUR si absente).</summary>
    public string? Devise { get; set; }

    /// <summary>Total HT d'entête stocké.</summary>
    public decimal TotalHt { get; set; }

    /// <summary>Total de TVA d'entête stocké.</summary>
    public decimal TotalTva { get; set; }

    /// <summary>Total TTC d'entête stocké.</summary>
    public decimal TotalTtc { get; set; }

    /// <summary>Nom de l'acheteur (destinataire), ou <c>null</c> en B2C anonyme.</summary>
    public string? ClientNom { get; set; }

    /// <summary>SIREN de l'acheteur si présent en source (transporté brut, jamais déduit).</summary>
    public string? ClientSiren { get; set; }

    /// <summary>Indice « société » brut de l'acheteur (aucune heuristique côté agent — VAL05).</summary>
    public bool ClientEstSociete { get; set; }

    /// <summary>Code postal de l'acheteur.</summary>
    public string? ClientCodePostal { get; set; }

    /// <summary>Ville de l'acheteur.</summary>
    public string? ClientVille { get; set; }

    /// <summary>Code pays ISO 3166-1 alpha-2 de l'acheteur.</summary>
    public string? ClientPays { get; set; }

    /// <summary>Lignes de la facture (jointure <c>lignes_facture</c>).</summary>
    public List<DemoErpALine> Lignes { get; } = new List<DemoErpALine>();
}
