namespace Liakont.Agent.Adapters.DemoErpB.Source;

using System;
using System.Collections.Generic;

/// <summary>
/// Reflet BRUT d'une ligne de la table <c>Invoice</c> de la base de DÉMONSTRATION DemoErpB (facturation
/// DÉNORMALISÉE fictive, anglais), avec ses lignes. Le type <c>Kind</c> « I »/« C » est transporté brut
/// (jamais classé ici). Montants en <c>float</c> (le schéma B simule un système legacy) → convertis en
/// <c>decimal</c> half-up à la frontière (ADR-0004 D3-7). Données 100 % fictives (CLAUDE.md n°7).
/// </summary>
internal sealed class DemoErpBInvoice
{
    /// <summary>Identifiant interne de la facture (<c>InvoiceId</c>) — clé de regroupement des lignes.</summary>
    public string? InvoiceId { get; set; }

    /// <summary>Numéro de pièce affiché (EN 16931 BT-1).</summary>
    public string? InvoiceNo { get; set; }

    /// <summary>Type de pièce source BRUT : « I » (invoice) ou « C » (credit note).</summary>
    public string? Kind { get; set; }

    /// <summary>Date d'émission (EN 16931 BT-2).</summary>
    public DateTime IssuedOn { get; set; }

    /// <summary>Pour un avoir : numéro de la facture d'origine rectifiée (EN 16931 BT-25).</summary>
    public string? OriginInvoiceNo { get; set; }

    /// <summary>Pour un avoir : date d'émission de la facture d'origine (auto-jointure), sinon <c>null</c>.</summary>
    public DateTime? OriginIssuedOn { get; set; }

    /// <summary>Devise ISO 4217 (défaut EUR si absente).</summary>
    public string? Currency { get; set; }

    /// <summary>Total HT d'entête stocké (flottant legacy).</summary>
    public double NetTotal { get; set; }

    /// <summary>Total de TVA d'entête stocké (flottant legacy).</summary>
    public double VatTotal { get; set; }

    /// <summary>Total TTC d'entête stocké (flottant legacy).</summary>
    public double GrossTotal { get; set; }

    /// <summary>Nom de l'acheteur, ou <c>null</c> en B2C anonyme.</summary>
    public string? BuyerName { get; set; }

    /// <summary>SIREN de l'acheteur si présent en source (transporté brut).</summary>
    public string? BuyerSiren { get; set; }

    /// <summary>Indice « société » brut de l'acheteur (aucune heuristique côté agent — VAL05).</summary>
    public bool BuyerIsCompany { get; set; }

    /// <summary>Lignes de la facture (jointure <c>InvoiceItem</c>).</summary>
    public List<DemoErpBItem> Items { get; } = new List<DemoErpBItem>();
}
