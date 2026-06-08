namespace Liakont.Host.Components;

using System;

/// <summary>
/// Libellé d'affichage FRANÇAIS du type d'un document pour la console (colonne « Type », F10 §2.1).
/// <see cref="Liakont.Modules.Documents.Domain.Entities.Document.DocumentType"/> est le type BRUT de la
/// source (Document.cs §commentaire) ; la classification fiscale facture/avoir réelle vit dans le module
/// Validation, PAS ici. Fonction TOTALE et PURE d'affichage (aucune règle métier interprétée,
/// CLAUDE.md n°2/19) : insensible à la casse, retombe sur la valeur brute pour un type non reconnu
/// (jamais masqué). Produit GÉNÉRIQUE : libellés neutres « Facture »/« Avoir » (le vocabulaire d'UN
/// client — ex. « bordereau » d'une salle des ventes — reste de la valeur source affichée telle quelle).
/// </summary>
public static class DocumentTypeDisplay
{
    /// <summary>
    /// Libellé français pour un type brut. <c>credit_note</c> / <c>creditnote</c> (toutes casses) → « Avoir » ;
    /// <c>invoice</c> → « Facture » ; toute autre valeur → la valeur brute non vide, ou « — » si vide.
    /// </summary>
    /// <param name="rawType">Type brut du document (ex. <c>invoice</c>, <c>credit_note</c>), tel que produit par la source.</param>
    public static string For(string? rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
        {
            return "—";
        }

        var normalized = rawType.Trim();

        // Avoir : on reconnaît EXACTEMENT la note de crédit EN16931 quelle que soit la casse/le séparateur
        // (credit_note, credit-note, creditNote…). Égalité stricte après normalisation : un type voisin
        // (credit_memo, creditcard…) n'est PAS un avoir et retombe sur sa valeur brute.
        var collapsed = normalized
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        if (collapsed.Equals("creditnote", StringComparison.OrdinalIgnoreCase))
        {
            return "Avoir";
        }

        if (normalized.Equals("invoice", StringComparison.OrdinalIgnoreCase))
        {
            return "Facture";
        }

        return normalized;
    }
}
