namespace Liakont.PaClients.ChorusPro;

using System.Globalization;
using System.Net;
using System.Text.Json;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.ChorusPro.Wire;

/// <summary>
/// Classe la réponse de la relecture <c>consulterCR</c> (F18 §4) en <see cref="PaDocumentStatus"/>. La
/// grille respecte la règle d'or de correction fiscale (CLAUDE.md n°3) : <see cref="PaSendState.Issued"/>
/// UNIQUEMENT sur le libellé <c>Intégré</c> ; tout le reste reste <see cref="PaSendState.Sending"/>
/// (transitoire) ou <see cref="PaSendState.RejectedByPa"/> ; <b>une valeur inconnue ne devient JAMAIS
/// <c>Issued</c></b> (défaut fail-safe). Les libellés <c>etatCourantFlux</c> (9 valeurs accentuées/casse
/// mixte) sont comparés EXACTEMENT (F18 §4). La réponse brute est TOUJOURS conservée pour l'audit
/// (<see cref="PaDocumentStatus.RawResponse"/>, F06/DR6) ; le retry transitoire est piloté par le client,
/// ce mapper ne fait que classer une réponse déjà obtenue. Modèle : <c>SuperPdpResponseMapper</c>.
/// </summary>
internal static class ChorusProStatusMapper
{
    // Les 9 libellés EXACTS etatCourantFlux (F18 §4 — Spec V5.00) → PaSendState. Comparaison ORDINALE :
    // les accents et la casse comptent (« Intégré », pas « integre »). Aucune valeur inventée (CLAUDE.md
    // n°2) : chaque entrée est tracée par F18 §4 avec sa justification de mapping.
    private static readonly Dictionary<string, PaSendState> EtatToState = new(StringComparer.Ordinal)
    {
        ["Reçu"] = PaSendState.Sending,                      // flux reçu, non encore intégré
        ["Traité SE CPP"] = PaSendState.Sending,            // en cours côté plateforme
        ["En attente de traitement"] = PaSendState.Sending, // attente
        ["En cours de traitement"] = PaSendState.Sending,   // en cours
        ["En attente de retraitement"] = PaSendState.Sending, // attente d'un retraitement
        ["Incidenté"] = PaSendState.RejectedByPa,           // flux NON traité, reprise opérateur (jamais re-dépôt auto, D8)
        ["Rejeté"] = PaSendState.RejectedByPa,              // rejet ; Errors + RawResponse intacts
        ["Intégré"] = PaSendState.Issued,                   // ✅ SEUL chemin vers Issued (A1/D5)
        ["Intégré partiellement"] = PaSendState.RejectedByPa, // intégration incomplète ≠ émis (C2, prudent)
    };

    /// <summary>Vrai si le code HTTP est une erreur TRANSITOIRE re-tentable (5xx — F18 §5).</summary>
    /// <param name="statusCode">Code HTTP de la réponse.</param>
    public static bool IsRetryableStatus(HttpStatusCode statusCode) => IsServerError(statusCode);

    /// <summary>
    /// Classe une réponse <c>consulterCR</c> en <see cref="PaDocumentStatus"/> (F18 §4-§5).
    /// </summary>
    /// <param name="statusCode">Code HTTP de la réponse.</param>
    /// <param name="rawBody">Corps brut (conservé pour l'audit, jamais perdu).</param>
    /// <param name="paDocumentId">Numéro de flux interrogé, conservé tel quel comme identifiant.</param>
    public static PaDocumentStatus MapDocumentStatus(HttpStatusCode statusCode, string rawBody, string paDocumentId)
    {
        // (1) 5xx + 401/403 → erreur TECHNIQUE re-tentable : jamais un rejet figé d'un document VALIDE (une
        // correction de credentials / une indispo passagère suffit). Le client a déjà retenté UNE fois sur 401.
        if (IsServerError(statusCode) || IsAuthError(statusCode))
        {
            return Status(paDocumentId, PaSendState.TechnicalError, HttpErrors(statusCode), rawBody);
        }

        // (2) 4xx métier → rejet, message générique FR + code HTTP ; RawResponse conserve le détail intact.
        if (!IsSuccess(statusCode))
        {
            return Status(paDocumentId, PaSendState.RejectedByPa, HttpErrors(statusCode), rawBody);
        }

        // (3) 2xx → classement par etatCourantFlux (F18 §4). Inconnu / absent → fail-safe Sending (on continue
        // de poller, JAMAIS Issued — CLAUDE.md n°3). Seuls les états de rejet portent une PaError de motif ;
        // les états transitoires n'en portent pas (la lecture suivante donnera l'état définitif).
        var etat = TryReadEtatCourantFlux(rawBody);
        var state = etat is not null && EtatToState.TryGetValue(etat, out var mapped)
            ? mapped
            : PaSendState.Sending;
        var errors = state == PaSendState.RejectedByPa ? RejectionErrors(etat!) : [];
        return Status(paDocumentId, state, errors, rawBody);
    }

    private static PaDocumentStatus Status(
        string paDocumentId, PaSendState state, IReadOnlyList<PaError> errors, string rawBody) =>
        new()
        {
            PaDocumentId = paDocumentId,
            State = state,
            Errors = errors,
            RawResponse = rawBody,
        };

    // Motif de rejet déduit du libellé etatCourantFlux (CLAUDE.md n°12 — message opérateur FR + action). Le
    // détail intégral reste dans RawResponse (le compte rendu Chorus Pro), jamais perdu.
    private static List<PaError> RejectionErrors(string etat) => etat switch
    {
        "Rejeté" => [new PaError(etat, "Flux rejeté par Chorus Pro — consultez le compte rendu d'intégration, corrigez puis redéposez.")],
        "Intégré partiellement" => [new PaError(etat, "Flux intégré partiellement par Chorus Pro (intégration incomplète) — reprise opérateur requise.")],
        "Incidenté" => [new PaError(etat, "Flux incidenté par Chorus Pro (non traité) — à rejouer entièrement par l'opérateur ; jamais de re-dépôt automatique (D8).")],
        _ => [],
    };

    // Erreur HTTP générique : code HTTP + message FR (le détail brut reste dans RawResponse). Pas de schéma
    // d'erreur Chorus Pro figé (❓ à verrouiller au Swagger, F18 §5) → aucune structure inventée (CLAUDE.md n°2).
    private static List<PaError> HttpErrors(HttpStatusCode statusCode)
    {
        var httpCode = ((int)statusCode).ToString(CultureInfo.InvariantCulture);
        var message = IsAuthError(statusCode)
            ? $"Erreur d'authentification/configuration Chorus Pro (HTTP {httpCode}) — re-tentable après correction des identifiants."
            : IsServerError(statusCode)
                ? $"Indisponibilité Chorus Pro (HTTP {httpCode}) — re-tentable au prochain run."
                : $"Relecture Chorus Pro refusée (HTTP {httpCode}) — voir la réponse brute.";
        return [new PaError(httpCode, message)];
    }

    private static string? TryReadEtatCourantFlux(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ChorusProConsulterCrResponse>(rawBody, ChorusProJson.Options);
            return string.IsNullOrWhiteSpace(parsed?.EtatCourantFlux) ? null : parsed!.EtatCourantFlux;
        }
        catch (JsonException)
        {
            // Corps non-JSON (page d'erreur HTML d'un proxy, etc.) : on ne perd pas la réponse brute, l'état
            // reste indéterminé → fail-safe Sending (jamais Issued).
            return null;
        }
    }

    private static bool IsSuccess(HttpStatusCode statusCode) => (int)statusCode is >= 200 and <= 299;

    private static bool IsServerError(HttpStatusCode statusCode) => (int)statusCode is >= 500 and <= 599;

    private static bool IsAuthError(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
