namespace Liakont.Host.Documents;

using System.Collections.Generic;
using System.Globalization;
using Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Résultat d'une re-vérification EN MASSE de la console (FIX207 — « Revérifier la sélection » / « Revérifier
/// tout »), présenté tel quel à l'opérateur. <see cref="Success"/> distingue l'aboutissement de l'opération d'un
/// refus (permission manquante) ; <see cref="Message"/> est le message opérateur en français avec les compteurs
/// (« N débloqués, N restés bloqués » — CLAUDE.md n°12) ; les décomptes individuels sont portés pour les tests
/// et un affichage éventuel. La trace d'audit fiscale FIX02 reste écrite PAR document par le cycle de vie.
/// </summary>
internal sealed record DocumentBulkRecheckResult(
    bool Success, int Total, int Unblocked, int StillBlocked, int Unavailable, int Skipped, string Message)
{
    /// <summary>Refus : l'opérateur ne porte pas <c>liakont.actions</c> (aucun document touché).</summary>
    public static DocumentBulkRecheckResult Denied() =>
        new(false, 0, 0, 0, 0, 0, "Action non autorisée : la permission « actions » (liakont.actions) est requise.");

    /// <summary>Rien à re-vérifier (aucun document bloqué dans le périmètre) : succès, message d'information.</summary>
    public static DocumentBulkRecheckResult Empty(string message) =>
        new(true, 0, 0, 0, 0, 0, message);

    /// <summary>Construit le résultat (et son message opérateur compteurs) à partir du décompte du cœur de re-vérification.</summary>
    public static DocumentBulkRecheckResult From(DocumentBulkRecheckSummary summary) =>
        new(true, summary.Total, summary.Unblocked, summary.StillBlocked, summary.Unavailable, summary.Skipped, BuildMessage(summary));

    private static string BuildMessage(DocumentBulkRecheckSummary s)
    {
        var culture = CultureInfo.CurrentCulture;
        var parts = new List<string>
        {
            string.Create(culture, $"{s.Unblocked} débloqué{Plural(s.Unblocked)}"),
            string.Create(culture, $"{s.StillBlocked} resté{Plural(s.StillBlocked)} bloqué{Plural(s.StillBlocked)}"),
        };

        if (s.Unavailable > 0)
        {
            parts.Add(string.Create(culture, $"{s.Unavailable} au contenu indisponible"));
        }

        if (s.Skipped > 0)
        {
            parts.Add(string.Create(culture, $"{s.Skipped} ignoré{Plural(s.Skipped)} (état déjà changé)"));
        }

        return string.Create(culture, $"Re-vérification de {s.Total} document{Plural(s.Total)} : {string.Join(", ", parts)}.");
    }

    private static string Plural(int count) => count > 1 ? "s" : string.Empty;
}
