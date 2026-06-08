namespace Liakont.Host.Components;

using System.Collections.Generic;
using Stratum.Common.UI.Models;

/// <summary>
/// Vocabulaire opérateur FRANÇAIS des états d'un document (F10 §2.2) : libellé + emoji + sévérité de
/// badge. Indexé par le NOM de l'état (la clé de <c>DocumentListResult.CountsByState</c>), ce qui garde
/// l'affichage DÉCOUPLÉ de l'enum du module Documents. Pur affichage : aucune règle métier interprétée
/// (CLAUDE.md n°2/19). Les emoji sont écrits en échappement Unicode pour être insensibles à l'encodage
/// du fichier source.
/// </summary>
public static class DocumentStateDisplay
{
    /// <summary>
    /// Ordre canonique d'affichage des états sur les synthèses de la console (de l'amont vers le
    /// terminal). Utilisé par le tableau de bord pour afficher chaque compteur même à zéro, afin que les
    /// états clés (À envoyer, Bloqué, Émis, Rejeté) soient toujours visibles.
    /// </summary>
    public static readonly IReadOnlyList<string> CanonicalOrder =
    [
        "Detected",
        "ReadyToSend",
        "Sending",
        "Blocked",
        "TechnicalError",
        "RejectedByPa",
        "Issued",
        "Superseded",
        "ManuallyHandled",
    ];

    /// <summary>
    /// Libellé français (avec emoji) et sévérité de badge pour un état. Fonction TOTALE : un état inconnu
    /// (ou vide) retombe sur un libellé neutre — jamais d'exception, jamais de couleur trompeuse.
    /// </summary>
    /// <param name="state">Nom de l'état (ex. <c>Issued</c>), tel que produit par le module Documents.</param>
    public static (string Label, Severity Severity) For(string? state) => state switch
    {
        // ⏳ À envoyer — lu dans le logiciel source, prêt (F10 : gris).
        "Detected" => ("⏳ À envoyer", Severity.Neutral),

        // 📤 Prêt à envoyer — état interne (non listé en F10 §2.2 ; libellé d'affichage uniquement).
        "ReadyToSend" => ("\U0001F4E4 Prêt à envoyer", Severity.Info),

        // ⏫ En cours — envoi engagé (F10 : bleu).
        "Sending" => ("⏫ En cours", Severity.Info),

        // 🛑 Bloqué — un contrôle a échoué avant envoi, action requise (F10 : orange).
        "Blocked" => ("\U0001F6D1 Bloqué", Severity.Warning),

        // ⚠️ Erreur technique — problème réseau/API, retenté automatiquement (F10 : rouge).
        "TechnicalError" => ("⚠️ Erreur technique", Severity.Error),

        // ❌ Rejeté — la PA a refusé, voir le motif (F10 : rouge).
        "RejectedByPa" => ("❌ Rejeté", Severity.Error),

        // ✅ Émis — accepté par la PA, tax report généré (F10 : vert).
        "Issued" => ("✅ Émis", Severity.Success),

        // ↪ Remplacé — renvoyé sous un autre numéro après rejet (F10 : gris clair).
        "Superseded" => ("↪ Remplacé", Severity.Neutral),

        // ✋ Traité manuellement — résolu hors passerelle par l'opérateur.
        "ManuallyHandled" => ("✋ Traité manuellement", Severity.Neutral),

        _ => (string.IsNullOrWhiteSpace(state) ? "—" : state!, Severity.Neutral),
    };
}
