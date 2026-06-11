namespace Liakont.PaClients.SuperPdp;

using System.Globalization;
using System.Net;
using System.Text.Json;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Mappe la réponse HTTP de Super PDP vers les DTOs neutres de l'abstraction (F14 §4-§4.1). Applique les
/// 3 familles d'erreurs (F14 §4.1, identiques au contrat F05 §4.1) dans l'ORDRE qui garantit la
/// correction fiscale (CLAUDE.md n°3) :
/// <list type="number">
///   <item>5xx (et réseau/timeout, traités par le client) → <see cref="PaSendState.TechnicalError"/> (re-tentable).</item>
///   <item>401/403 (auth/config OAuth) → <see cref="PaSendState.TechnicalError"/> re-tentable (jamais un rejet métier figé).</item>
///   <item><c>errors[]</c> non vide, MÊME sur HTTP 200 (erreur silencieuse, O6) → <see cref="PaSendState.RejectedByPa"/> — surtout PAS « émis ».</item>
///   <item>2xx propre → état réel (seul <c>issued</c> est une émission) ; 4xx sans <c>errors[]</c> → rejet (pas de retry).</item>
/// </list>
/// La réponse brute est TOUJOURS conservée (piste d'audit F06/DR6). Le retry/backoff est piloté par le
/// client ; ce mapper ne fait que classer une réponse déjà obtenue.
/// </summary>
internal static class SuperPdpResponseMapper
{
    /// <summary>Classe une réponse d'émission de facture en <see cref="PaSendResult"/>.</summary>
    /// <param name="statusCode">Code HTTP de la réponse.</param>
    /// <param name="rawBody">Corps brut de la réponse (conservé pour l'audit, jamais perdu).</param>
    public static PaSendResult MapSendResult(HttpStatusCode statusCode, string rawBody)
    {
        var parsed = TryParse(rawBody);
        var errors = MapErrors(parsed?.Errors);

        // (1) 5xx → erreur technique re-tentable, avant toute lecture d'errors[].
        if (IsServerError(statusCode))
        {
            return PaSendResult.Technical(errors, rawBody);
        }

        // (2) 401/403 = erreur d'AUTHENTIFICATION/configuration OAuth (jeton/identifiants), PAS un rejet
        // métier du document : classée re-tentable (TechnicalError) plutôt que RejectedByPa terminal —
        // un rejet figerait des documents VALIDES qu'il faudrait recréer alors qu'une correction de
        // credentials suffit. Le client a déjà retenté UNE fois avec un jeton rafraîchi avant d'arriver ici.
        if (IsAuthError(statusCode))
        {
            var authErrors = errors.Count > 0
                ? errors
                : new List<PaError>
                {
                    new(
                        ((int)statusCode).ToString(CultureInfo.InvariantCulture),
                        $"Erreur d'authentification/configuration Super PDP (HTTP {(int)statusCode}) — re-tentable après correction des identifiants OAuth."),
                };
            return PaSendResult.Technical(authErrors, rawBody);
        }

        // (3) errors[] non vide (4xx OU 200 silencieux) → rejet, jamais une émission (F14 §4.1).
        if (errors.Count > 0)
        {
            return PaSendResult.Rejected(errors, paDocumentId: parsed?.Id, rawResponse: rawBody);
        }

        // (4) 2xx : l'ÉTAT RÉEL pilote le résultat. Un document « new » (créé sans envoi) ou « sending »
        // (envoi async en cours) n'est PAS « émis » : ne jamais compter émis un document non transmis
        // (correction fiscale/audit — CLAUDE.md n°3).
        if (IsSuccess(statusCode))
        {
            if (string.IsNullOrWhiteSpace(parsed?.Id))
            {
                return PaSendResult.Technical(
                    [new PaError("SPDP_NO_ID", "Réponse 2xx sans identifiant ni erreur — émission non confirmée.")],
                    rawBody);
            }

            return MapSuccessState(parsed!, rawBody);
        }

        // (5) 4xx métier sans errors[] (ex. 404, 422 sans corps détaillé) → rejet, pas de retry. Les
        // codes d'AUTH (401/403) sont déjà traités en (2) ; ce rejet est document-level.
        var httpCode = (int)statusCode;
        var statusError = new PaError(
            httpCode.ToString(CultureInfo.InvariantCulture),
            $"Rejet Super PDP (HTTP {httpCode}).");
        return PaSendResult.Rejected([statusError], rawResponse: rawBody);
    }

    /// <summary>
    /// Classe une réponse de RELECTURE d'état (<c>GET /v1.beta/invoices/{id}</c>, F14 §4) en
    /// <see cref="PaDocumentStatus"/>, avec la MÊME grille que l'émission. Le retry transitoire est piloté
    /// par le client ; ce mapper classe la réponse FINALE obtenue.
    /// </summary>
    /// <param name="statusCode">Code HTTP de la réponse.</param>
    /// <param name="rawBody">Corps brut (conservé pour l'audit, jamais perdu).</param>
    /// <param name="paDocumentId">Identifiant interrogé, repli si la réponse ne le renvoie pas.</param>
    public static PaDocumentStatus MapDocumentStatus(HttpStatusCode statusCode, string rawBody, string paDocumentId)
    {
        var parsed = TryParse(rawBody);
        var errors = MapErrors(parsed?.Errors);
        var taxReportIds = MapTaxReportIds(parsed?.TaxReportIds);

        PaSendState state;
        if (IsServerError(statusCode) || IsAuthError(statusCode))
        {
            state = PaSendState.TechnicalError;
        }
        else if (errors.Count > 0)
        {
            state = PaSendState.RejectedByPa;
        }
        else if (IsSuccess(statusCode))
        {
            state = StateFromString(parsed?.State);
        }
        else
        {
            errors =
            [
                new PaError(
                    ((int)statusCode).ToString(CultureInfo.InvariantCulture),
                    $"Relecture Super PDP en échec (HTTP {(int)statusCode})."),
            ];
            state = PaSendState.RejectedByPa;
        }

        return new PaDocumentStatus
        {
            PaDocumentId = string.IsNullOrWhiteSpace(parsed?.Id) ? paDocumentId : parsed!.Id!,
            State = state,
            TaxReportIds = taxReportIds,
            Errors = errors,
            RawResponse = rawBody,
        };
    }

    /// <summary>
    /// Construit le résultat d'émission RACCROCHÉ à une facture retrouvée dans la liste du compte lors de
    /// la relecture d'idempotence (F14 §4.1) : on ne ré-émet pas, on rattache l'état réel de la facture
    /// déjà créée. Une facture retrouvée AVEC <c>errors[]</c> reste un rejet (pas une émission).
    /// </summary>
    /// <param name="invoice">Facture retrouvée (doit porter un identifiant).</param>
    /// <param name="rawBody">Corps brut de la liste, conservé pour l'audit.</param>
    public static PaSendResult MapReconnected(SuperPdpInvoiceResponse invoice, string rawBody)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var errors = MapErrors(invoice.Errors);
        if (errors.Count > 0)
        {
            return PaSendResult.Rejected(errors, invoice.Id, rawBody);
        }

        if (string.IsNullOrWhiteSpace(invoice.Id))
        {
            return PaSendResult.Technical(
                [new PaError("SPDP_NO_ID", "Facture retrouvée sans identifiant — raccrochage impossible.")],
                rawBody);
        }

        return MapSuccessState(invoice, rawBody);
    }

    /// <summary>
    /// Parse défensivement une liste de factures (relecture d'idempotence, F14 §4.1). Tolère deux formes :
    /// un tableau JSON nu OU l'enveloppe <c>{ "invoices": [...] }</c> (forme exacte à confirmer sandbox —
    /// PAS03, O2). Toute forme non reconnue retourne <c>false</c> : le résultat est alors NON CONCLUANT
    /// côté client (on n'en déduit JAMAIS « facture absente » — pas de ré-émission à l'aveugle qui créerait
    /// un doublon, CLAUDE.md n°3).
    /// </summary>
    /// <param name="rawBody">Corps brut de la réponse de liste.</param>
    /// <param name="invoices">Les factures parsées si la forme est reconnue ; vide sinon.</param>
    /// <returns><c>true</c> si une liste a pu être parsée (même vide), <c>false</c> si non concluant.</returns>
    public static bool TryParseInvoiceList(string rawBody, out IReadOnlyList<SuperPdpInvoiceResponse> invoices)
    {
        invoices = [];
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            switch (document.RootElement.ValueKind)
            {
                case JsonValueKind.Array:
                    invoices = JsonSerializer.Deserialize<List<SuperPdpInvoiceResponse>>(rawBody, SuperPdpJson.Options)
                        ?? [];
                    return true;
                case JsonValueKind.Object:
                    var wrapped = JsonSerializer.Deserialize<SuperPdpInvoiceListResponse>(rawBody, SuperPdpJson.Options);
                    if (wrapped?.Invoices is null)
                    {
                        return false;
                    }

                    invoices = wrapped.Invoices;
                    return true;
                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Vrai si le code HTTP est une erreur TRANSITOIRE re-tentable (5xx — F14 §4.1).</summary>
    public static bool IsRetryableStatus(HttpStatusCode statusCode) => IsServerError(statusCode);

    // Mappe une chaîne d'état Super PDP (cible F14 §4) vers l'état neutre. Jamais « émis » par défaut : un
    // état inconnu reste « en cours » (Sending), « error » est un rejet — correction fiscale/audit.
    private static PaSendState StateFromString(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "issued" => PaSendState.Issued,
        "sending" => PaSendState.Sending,
        "new" => PaSendState.New,
        "error" => PaSendState.RejectedByPa,
        _ => PaSendState.Sending,
    };

    // Mappe l'état d'une réponse 2xx SANS errors[]. Seul « issued » est une ÉMISSION ; « new »/« sending »
    // restent intermédiaires (non facturables), « error » est un rejet, un état inconnu reste « en cours »
    // — JAMAIS « émis » par défaut (CLAUDE.md n°3).
    private static PaSendResult MapSuccessState(SuperPdpInvoiceResponse parsed, string rawBody)
    {
        var id = parsed.Id!;
        var taxReportIds = MapTaxReportIds(parsed.TaxReportIds);
        return parsed.State?.Trim().ToLowerInvariant() switch
        {
            "issued" => PaSendResult.Issued(id, taxReportIds, rawBody),
            "sending" => InProgress(PaSendState.Sending, id, taxReportIds, rawBody),
            "new" => InProgress(PaSendState.New, id, taxReportIds, rawBody),
            "error" => PaSendResult.Rejected(
                [new PaError("SPDP_STATE_ERROR", "État Super PDP « error » sans détail d'erreur.")],
                id,
                rawBody),
            _ => InProgress(PaSendState.Sending, id, taxReportIds, rawBody),
        };
    }

    private static PaSendResult InProgress(
        PaSendState state,
        string id,
        IReadOnlyList<string> taxReportIds,
        string rawBody) => new()
        {
            State = state,
            PaDocumentId = id,
            TaxReportIds = taxReportIds,
            RawResponse = rawBody,
        };

    private static SuperPdpInvoiceResponse? TryParse(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SuperPdpInvoiceResponse>(rawBody, SuperPdpJson.Options);
        }
        catch (JsonException)
        {
            // Corps non-JSON (page d'erreur HTML d'un proxy, etc.) : on ne perd pas la réponse brute, le
            // mapper retombe sur la classification par code HTTP.
            return null;
        }
    }

    private static List<PaError> MapErrors(IReadOnlyList<SuperPdpError>? errors) =>
        errors is null || errors.Count == 0
            ? []
            : errors.Select(e => new PaError(e.Code ?? string.Empty, e.Message ?? string.Empty)).ToList();

    private static List<string> MapTaxReportIds(IReadOnlyList<string>? ids) =>
        ids is null || ids.Count == 0 ? [] : ids.ToList();

    private static bool IsSuccess(HttpStatusCode statusCode) =>
        (int)statusCode is >= 200 and <= 299;

    private static bool IsServerError(HttpStatusCode statusCode) =>
        (int)statusCode is >= 500 and <= 599;

    private static bool IsAuthError(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
