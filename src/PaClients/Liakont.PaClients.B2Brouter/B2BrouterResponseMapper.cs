namespace Liakont.PaClients.B2Brouter;

using System.Globalization;
using System.Net;
using System.Text.Json;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.B2Brouter.Wire;

/// <summary>
/// Mappe la réponse HTTP de B2Brouter vers les DTOs neutres de l'abstraction (F05 §3-§4.1). Applique
/// les 3 familles d'erreurs de F05 §4.1 dans l'ORDRE qui garantit la correction fiscale (CLAUDE.md n°3) :
/// <list type="number">
///   <item>5xx (et réseau/timeout, traités par le client) → <see cref="PaSendState.TechnicalError"/> (re-tentable).</item>
///   <item><c>errors[]</c> non vide, MÊME sur HTTP 200 (erreur silencieuse) → <see cref="PaSendState.RejectedByPa"/> — surtout PAS « émis ».</item>
///   <item>2xx propre → <see cref="PaSendState.Issued"/> ; 4xx sans <c>errors[]</c> → rejet (pas de retry).</item>
/// </list>
/// La réponse brute est TOUJOURS conservée (piste d'audit F06/DR6). Le retry/backoff lui-même est
/// ajouté par PAB02 ; ce mapper ne fait que classer une réponse déjà obtenue.
/// </summary>
internal static class B2BrouterResponseMapper
{
    /// <summary>Classe une réponse d'envoi (création de facture) en <see cref="PaSendResult"/>.</summary>
    /// <param name="statusCode">Code HTTP de la réponse.</param>
    /// <param name="rawBody">Corps brut de la réponse (conservé pour l'audit, jamais perdu).</param>
    public static PaSendResult MapSendResult(HttpStatusCode statusCode, string rawBody)
    {
        var parsed = TryParse(rawBody);
        var errors = MapErrors(parsed?.Errors);

        // (1) 5xx → erreur technique re-tentable (F05 §4.1), avant toute lecture d'errors[].
        if (IsServerError(statusCode))
        {
            return PaSendResult.Technical(errors, rawBody);
        }

        // (2) errors[] non vide (4xx OU 200 silencieux) → rejet, jamais une émission (F05 §4.1).
        if (errors.Count > 0)
        {
            return PaSendResult.Rejected(errors, paDocumentId: parsed?.Id, rawResponse: rawBody);
        }

        // (3) 2xx propre → émis (si la PA a bien attribué un identifiant).
        if (IsSuccess(statusCode))
        {
            return string.IsNullOrWhiteSpace(parsed?.Id)
                ? PaSendResult.Technical(
                    [new PaError("B2B_NO_ID", "Réponse 2xx sans identifiant ni erreur — émission non confirmée.")],
                    rawBody)
                : PaSendResult.Issued(parsed!.Id!, MapTaxReportIds(parsed.TaxReportIds), rawBody);
        }

        // 4xx sans errors[] (ex. 401/404 sans corps détaillé) → rejet, pas de retry (F05 §4.1).
        var httpCode = (int)statusCode;
        var statusError = new PaError(
            httpCode.ToString(CultureInfo.InvariantCulture),
            $"Rejet B2Brouter (HTTP {httpCode}).");
        return PaSendResult.Rejected([statusError], rawResponse: rawBody);
    }

    private static B2BrouterInvoiceResponse? TryParse(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<B2BrouterInvoiceResponse>(rawBody, B2BrouterJson.Options);
        }
        catch (JsonException)
        {
            // Corps non-JSON (page d'erreur HTML d'un proxy, etc.) : on ne perd pas la réponse brute,
            // le mapper retombe sur la classification par code HTTP.
            return null;
        }
    }

    private static List<PaError> MapErrors(IReadOnlyList<B2BrouterError>? errors) =>
        errors is null || errors.Count == 0
            ? []
            : errors.Select(e => new PaError(e.Code ?? string.Empty, e.Message ?? string.Empty)).ToList();

    private static List<string> MapTaxReportIds(IReadOnlyList<string>? ids) =>
        ids is null || ids.Count == 0 ? [] : ids.ToList();

    private static bool IsSuccess(HttpStatusCode statusCode) =>
        (int)statusCode is >= 200 and <= 299;

    private static bool IsServerError(HttpStatusCode statusCode) =>
        (int)statusCode is >= 500 and <= 599;
}
