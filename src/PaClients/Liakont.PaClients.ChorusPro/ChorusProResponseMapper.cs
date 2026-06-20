namespace Liakont.PaClients.ChorusPro;

using System.Net;
using System.Text.Json;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Classe la réponse HTTP du dépôt <c>deposerFluxFacture</c> en <see cref="PaSendResult"/> (F18 §3.4/§5).
/// Règle d'or (CLAUDE.md n°3) : le dépôt accepté n'est <b>JAMAIS</b> <see cref="PaSendState.Issued"/> —
/// <c>numeroFluxDepot</c> est un accusé de RÉCEPTION, pas une preuve d'intégration (A1/D5). L'intégration
/// réelle (<c>etatCourantFlux = Intégré</c>) se relit par <c>consulterCR</c> (CP05). Le mapping est
/// <b>fail-safe</b> : tout corps illisible ou ambigu dégrade vers un état SÛR (jamais un faux « émis »).
/// <list type="bullet">
///   <item>2xx + <c>numeroFluxDepot</c> présent → <see cref="PaSendState.Sending"/> (PaDocumentId = le numéro de flux).</item>
///   <item>2xx SANS <c>numeroFluxDepot</c> → <see cref="PaSendState.RejectedByPa"/> (rejet métier silencieux ; <c>libelle</c>/<c>codeRetour</c> conservés).</item>
///   <item>5xx / 401 / 403 / 408 / 429 → <see cref="PaSendState.TechnicalError"/> (re-tentable, SANS re-dépôt — A3/D8).</item>
///   <item>autres 4xx → <see cref="PaSendState.RejectedByPa"/> (rejet métier, <c>PaError</c> intacts).</item>
/// </list>
/// <see cref="PaSendResult.RawResponse"/> conserve le corps de RÉPONSE pour l'audit (F06) — il ne porte
/// aucun credential (les secrets <c>Authorization</c> / <c>cpro-account</c> sont des en-têtes de REQUÊTE,
/// jamais journalisés — CLAUDE.md n°10). <c>internal static</c> : exercé en test, jamais exposé.
/// </summary>
internal static class ChorusProResponseMapper
{
    /// <summary>
    /// Indique si un code HTTP est RE-TENTABLE (transitoire) : 5xx, ou auth/quota/timeout (401/403/408/429).
    /// F18 §5 : « 5xx / 401 / 403 / timeout → Technical (re-tentable, sans re-POST de dépôt) ». Le 401/403
    /// est classé re-tentable (jamais un rejet métier figé : config/jeton, pas le document).
    /// </summary>
    /// <param name="statusCode">Code HTTP de la réponse Chorus Pro.</param>
    /// <returns><c>true</c> si l'appel doit être re-tenté au prochain run (sans re-déposer le flux).</returns>
    public static bool IsRetryableStatus(HttpStatusCode statusCode) =>
        (int)statusCode >= 500
        || statusCode is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests;

    /// <summary>
    /// Mappe le résultat du dépôt <c>deposerFluxFacture</c> (F18 §3.4/§5) en <see cref="PaSendResult"/>.
    /// </summary>
    /// <param name="statusCode">Code HTTP de la réponse.</param>
    /// <param name="rawBody">Corps brut de la réponse (conservé pour l'audit — sans credential).</param>
    /// <returns>Le résultat classé (Sending / RejectedByPa / TechnicalError), jamais Issued au dépôt.</returns>
    public static PaSendResult MapDeposit(HttpStatusCode statusCode, string rawBody)
    {
        // Transitoire (5xx, auth, quota, timeout) → re-tentable. JAMAIS de re-dépôt automatique (A3/D8) :
        // l'appelant ne re-poste pas, la reprise est opérateur (sinon double flux = double facture, n°3).
        if (IsRetryableStatus(statusCode))
        {
            return PaSendResult.Technical(BuildErrors(statusCode, rawBody, "CPRO_TECHNIQUE"), rawBody);
        }

        // 4xx métier (hors 401/403/408/429 déjà classés transitoires) : rejet, messages Chorus Pro intacts.
        if ((int)statusCode >= 400)
        {
            return PaSendResult.Rejected(BuildErrors(statusCode, rawBody, "CPRO_REJET"), rawResponse: rawBody);
        }

        // 2xx : le dépôt n'est acquis QUE si Chorus Pro a délivré un accusé de réception (numeroFluxDepot).
        var fluxNumber = TryReadFluxNumber(rawBody);
        if (!string.IsNullOrWhiteSpace(fluxNumber))
        {
            // Accusé de RÉCEPTION → Sending (PaDocumentId = numeroFluxDepot). JAMAIS Issued au dépôt :
            // l'intégration réelle se confirme par consulterCR (CP05) — règle d'or A1/D5 (CLAUDE.md n°3).
            return new PaSendResult
            {
                State = PaSendState.Sending,
                PaDocumentId = fluxNumber,
                RawResponse = rawBody,
            };
        }

        // 2xx SANS numéro de flux = rejet métier SILENCIEUX (codeRetour KO / errors[]) : surtout pas Sending
        // (on n'a aucun accusé à relire), surtout pas Issued. Rejet, libelle/codeRetour conservés.
        return PaSendResult.Rejected(BuildErrors(statusCode, rawBody, "CPRO_REJET_SILENCIEUX"), rawResponse: rawBody);
    }

    // Construit les PaError en préservant le message Chorus Pro (libelle/message) et son codeRetour quand ils
    // sont lisibles ; sinon un code par défaut + le corps brut (jamais perdre l'information d'audit). Parsing
    // fail-safe : un corps non-JSON ne lève pas (un dépôt ne doit pas planter sur une forme inattendue).
    private static IReadOnlyList<PaError> BuildErrors(HttpStatusCode statusCode, string rawBody, string fallbackCode)
    {
        var (code, message) = TryReadError(rawBody);
        return
        [
            new PaError(
                code ?? fallbackCode,
                message ?? $"Dépôt Chorus Pro — réponse HTTP {(int)statusCode}. Corps : {Truncate(rawBody)}"),
        ];
    }

    // Lit numeroFluxDepot (accusé de réception du flux, F18 §3.4) — accepte une chaîne OU un nombre JSON.
    // Renvoie null si le corps est illisible ou le champ absent (→ pas d'accusé = pas de Sending).
    private static string? TryReadFluxNumber(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            if (!doc.RootElement.TryGetProperty("numeroFluxDepot", out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Lit le couple (codeRetour, libelle/message) d'un corps de réponse Chorus Pro, fail-safe. codeRetour
    // peut être une chaîne ou un nombre ; le message se lit sur libelle (champ documenté F18 §3.4) ou, à
    // défaut, message. Renvoie (null, null) si rien d'exploitable.
    private static (string? Code, string? Message) TryReadError(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            string? code = null;
            if (root.TryGetProperty("codeRetour", out var codeElement))
            {
                code = codeElement.ValueKind switch
                {
                    JsonValueKind.String => codeElement.GetString(),
                    JsonValueKind.Number => codeElement.GetRawText(),
                    _ => null,
                };
            }

            string? message = null;
            if (root.TryGetProperty("libelle", out var libelle) && libelle.ValueKind == JsonValueKind.String)
            {
                message = libelle.GetString();
            }
            else if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
            {
                message = msg.GetString();
            }

            return (string.IsNullOrWhiteSpace(code) ? null : code, string.IsNullOrWhiteSpace(message) ? null : message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string Truncate(string value) =>
        string.IsNullOrEmpty(value) || value.Length <= 500 ? value : value[..500];
}
