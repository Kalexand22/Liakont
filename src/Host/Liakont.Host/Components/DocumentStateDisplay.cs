namespace Liakont.Host.Components;

using System.Collections.Generic;
using Stratum.Common.UI.Models;

/// <summary>
/// Vocabulaire opérateur FRANÇAIS des états d'un document (F10 §2.2) : libellé + emoji + sévérité de
/// badge. Indexé par le NOM de l'état (la clé de <c>DocumentListResult.CountsByState</c>), ce qui garde
/// l'affichage DÉCOUPLÉ de l'enum du module Documents. Pur affichage : aucune règle métier interprétée
/// (CLAUDE.md n°2/19). Tous les emoji des libellés sont écrits en échappement Unicode C# (<c>\U…</c>),
/// donc insensibles à l'encodage du fichier source ; les commentaires les annotent en notation U+.
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
        // U+23F3 (sablier) À envoyer — lu dans le logiciel source, prêt (F10 : gris).
        "Detected" => ("\U000023F3 À envoyer", Severity.Neutral),

        // U+1F4E4 (bac d'envoi) Prêt à envoyer — état interne (non listé en F10 §2.2 ; libellé d'affichage).
        "ReadyToSend" => ("\U0001F4E4 Prêt à envoyer", Severity.Info),

        // U+23EB (double flèche haute) En cours — envoi engagé (F10 : bleu).
        "Sending" => ("\U000023EB En cours", Severity.Info),

        // U+1F6D1 (panneau stop) Bloqué — un contrôle a échoué avant envoi, action requise (F10 : orange).
        "Blocked" => ("\U0001F6D1 Bloqué", Severity.Warning),

        // U+26A0 U+FE0F (avertissement) Erreur technique — problème réseau/API, retenté (F10 : rouge).
        "TechnicalError" => ("\U000026A0\U0000FE0F Erreur technique", Severity.Error),

        // U+274C (croix) Rejeté — la PA a refusé, voir le motif (F10 : rouge).
        "RejectedByPa" => ("\U0000274C Rejeté", Severity.Error),

        // U+2705 (coche verte) Émis — accepté par la PA, tax report généré (F10 : vert).
        "Issued" => ("\U00002705 Émis", Severity.Success),

        // U+21AA (flèche crochet) Remplacé — renvoyé sous un autre numéro après rejet (F10 : gris clair).
        "Superseded" => ("\U000021AA Remplacé", Severity.Neutral),

        // U+270B (main levée) Traité manuellement — résolu hors passerelle par l'opérateur.
        "ManuallyHandled" => ("\U0000270B Traité manuellement", Severity.Neutral),

        _ => (string.IsNullOrWhiteSpace(state) ? "—" : state!, Severity.Neutral),
    };
}
