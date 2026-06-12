namespace Liakont.PaClients.SuperPdp;

using System.Globalization;
using System.Net;
using System.Text.Json;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Mappe les réponses HTTP de Super PDP vers les DTOs neutres de l'abstraction (F14 §4-§4.1, ✅ contrat
/// confirmé OpenAPI + sandbox 2026-06-12). Trois familles d'erreurs dans l'ORDRE qui garantit la
/// correction fiscale (CLAUDE.md n°3) :
/// <list type="number">
///   <item>5xx (et réseau/timeout, traités par le client) → <see cref="PaSendState.TechnicalError"/> (re-tentable).</item>
///   <item>401/403 (auth/config OAuth) → <see cref="PaSendState.TechnicalError"/> re-tentable (jamais un rejet métier figé).</item>
///   <item>4xx → <see cref="PaSendState.RejectedByPa"/>, message Super PDP (<c>{"http_status_code","message"}</c>) conservé INTACT.</item>
///   <item>2xx → classement par les ÉVÉNEMENTS (<c>events[]</c>) : l'envoi est ASYNCHRONE, un 200 signifie
///   « téléversée », JAMAIS « émise » (F14 §4.1) — le piège silencieux réel de cette API.</item>
/// </list>
/// La réponse brute est TOUJOURS conservée (piste d'audit F06/DR6). Le retry/backoff est piloté par le
/// client ; ce mapper ne fait que classer une réponse déjà obtenue.
/// </summary>
internal static class SuperPdpResponseMapper
{
    // Classement des status_code d'événement (énumération FERMÉE de l'OpenAPI v1.24.0.beta — F14 §4.1).
    // ÉCHEC terminal SANS émission : prioritaire sur tout le reste (prudence fiscale, CLAUDE.md n°3).
    private static readonly HashSet<string> FailureEventCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "api:invalid",   // erreur AVANT transmission au point d'accès
        "api:rejected",  // rejet asynchrone par l'autre point d'accès
        "fr:213",        // Rejetée (cycle de vie officiel français)
        "fr:501",        // Inadmissible
    };

    // ÉMISSION confirmée ou événement POSTÉRIEUR à l'émission. fr:210 (Refusée par le destinataire) et
    // fr:207 (En litige) impliquent une facture ÉMISE fiscalement : le refus métier est un processus aval,
    // hors PaSendState (F14 §4.1) — entièrement tracé dans RawResponse.
    private static readonly HashSet<string> IssuedEventCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "api:sent", "api:received", "api:acknowledged", "api:accepted",
        "fr:201", "fr:202", "fr:203", "fr:204", "fr:205", "fr:206",
        "fr:207", "fr:208", "fr:209", "fr:210", "fr:211", "fr:212",
    };

    /// <summary>
    /// Classe la réponse de l'émission (<c>POST /v1.beta/invoices</c>, corps = la ressource
    /// <c>invoice</c>) en <see cref="PaSendResult"/>.
    /// </summary>
    /// <param name="statusCode">Code HTTP de la réponse.</param>
    /// <param name="rawBody">Corps brut de la réponse (conservé pour l'audit, jamais perdu).</param>
    public static PaSendResult MapSendResult(HttpStatusCode statusCode, string rawBody)
    {
        // (1) 5xx → erreur technique re-tentable.
        if (IsServerError(statusCode))
        {
            return PaSendResult.Technical(ParseErrors(rawBody, statusCode), rawBody);
        }

        // (2) 401/403 = erreur d'AUTHENTIFICATION/configuration OAuth (jeton/identifiants), PAS un rejet
        // métier du document : classée re-tentable (TechnicalError) plutôt que RejectedByPa terminal —
        // un rejet figerait des documents VALIDES qu'il faudrait recréer alors qu'une correction de
        // credentials suffit. Le client a déjà retenté UNE fois avec un jeton rafraîchi avant d'arriver ici.
        if (IsAuthError(statusCode))
        {
            return PaSendResult.Technical(AuthErrors(statusCode, rawBody), rawBody);
        }

        // (3) 4xx → rejet métier, message Super PDP intact (règle BR-* du converter, vendeur ≠ compte,
        // buyer non adressable… — F14 §4.1), pas de retry.
        if (!IsSuccess(statusCode))
        {
            return PaSendResult.Rejected(ParseErrors(rawBody, statusCode), rawResponse: rawBody);
        }

        // (4) 2xx : la ressource invoice pilote le résultat — l'envoi est ASYNCHRONE, l'état réel vient
        // des events[] (jamais « émis » sur le seul code HTTP — CLAUDE.md n°3).
        var parsed = TryParse(rawBody);
        if (parsed?.Id is null)
        {
            return PaSendResult.Technical(
                [new PaError("SPDP_NO_ID", "Réponse 2xx sans identifiant de facture — émission non confirmée.")],
                rawBody);
        }

        return FromInvoice(parsed, rawBody);
    }

    /// <summary>
    /// Classe une réponse de RELECTURE d'état (<c>GET /v1.beta/invoices/{id}</c>, F14 §3.4) en
    /// <see cref="PaDocumentStatus"/>, avec la MÊME grille que l'émission. Le retry transitoire est piloté
    /// par le client ; ce mapper classe la réponse FINALE obtenue.
    /// </summary>
    /// <param name="statusCode">Code HTTP de la réponse.</param>
    /// <param name="rawBody">Corps brut (conservé pour l'audit, jamais perdu).</param>
    /// <param name="paDocumentId">Identifiant interrogé, repli si la réponse ne le renvoie pas.</param>
    public static PaDocumentStatus MapDocumentStatus(HttpStatusCode statusCode, string rawBody, string paDocumentId)
    {
        if (IsServerError(statusCode) || IsAuthError(statusCode))
        {
            return new PaDocumentStatus
            {
                PaDocumentId = paDocumentId,
                State = PaSendState.TechnicalError,
                Errors = ParseErrors(rawBody, statusCode),
                RawResponse = rawBody,
            };
        }

        if (!IsSuccess(statusCode))
        {
            return new PaDocumentStatus
            {
                PaDocumentId = paDocumentId,
                State = PaSendState.RejectedByPa,
                Errors = ParseErrors(rawBody, statusCode),
                RawResponse = rawBody,
            };
        }

        var parsed = TryParse(rawBody);
        var state = StateFromEvents(parsed?.Events);
        return new PaDocumentStatus
        {
            PaDocumentId = parsed?.Id is null
                ? paDocumentId
                : parsed.Id.Value.ToString(CultureInfo.InvariantCulture),
            State = state,
            Errors = state == PaSendState.RejectedByPa ? FailureErrors(parsed?.Events) : [],
            RawResponse = rawBody,
        };
    }

    /// <summary>
    /// Classe l'ÉCHEC de la conversion <c>POST /v1.beta/invoices/convert</c> (F14 §3.2) : la conversion ne
    /// crée RIEN côté PA, sa réussite (200 = le XML CII) est traitée par le client. Un 4xx porte les
    /// messages des règles EN 16931 (<c>BR-*</c>) — conservés INTACTS pour l'opérateur.
    /// </summary>
    /// <param name="statusCode">Code HTTP non-2xx de la conversion.</param>
    /// <param name="rawBody">Corps brut de l'erreur (conservé pour l'audit).</param>
    public static PaSendResult MapConvertFailure(HttpStatusCode statusCode, string rawBody)
    {
        if (IsServerError(statusCode))
        {
            return PaSendResult.Technical(ParseErrors(rawBody, statusCode), rawBody);
        }

        if (IsAuthError(statusCode))
        {
            return PaSendResult.Technical(AuthErrors(statusCode, rawBody), rawBody);
        }

        return PaSendResult.Rejected(ParseErrors(rawBody, statusCode), rawResponse: rawBody);
    }

    /// <summary>
    /// Construit le résultat d'émission RACCROCHÉ à une facture retrouvée dans la liste du compte lors de
    /// la relecture d'idempotence (F14 §4.1) : on ne ré-émet pas, on rattache l'état réel de la facture
    /// déjà créée (classé par ses events, comme toute ressource invoice).
    /// </summary>
    /// <param name="invoice">Facture retrouvée (doit porter un identifiant).</param>
    /// <param name="rawBody">Corps brut de la liste, conservé pour l'audit.</param>
    public static PaSendResult MapReconnected(SuperPdpInvoiceResponse invoice, string rawBody)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (invoice.Id is null)
        {
            return PaSendResult.Technical(
                [new PaError("SPDP_NO_ID", "Facture retrouvée sans identifiant — raccrochage impossible.")],
                rawBody);
        }

        return FromInvoice(invoice, rawBody);
    }

    /// <summary>
    /// Parse défensivement la liste paginée de <c>GET /v1.beta/invoices</c> (✅ forme confirmée :
    /// <c>{"data":[…]}</c> — F14 §3.2). Toute forme non reconnue retourne <c>false</c> : le résultat est
    /// alors NON CONCLUANT côté client (on n'en déduit JAMAIS « facture absente » — pas de ré-émission à
    /// l'aveugle qui créerait un doublon, CLAUDE.md n°3).
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
            var wrapped = JsonSerializer.Deserialize<SuperPdpInvoiceListResponse>(rawBody, SuperPdpJson.Options);
            if (wrapped?.Data is null)
            {
                return false;
            }

            invoices = wrapped.Data;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Vrai si le code HTTP est une erreur TRANSITOIRE re-tentable (5xx — F14 §4.1).</summary>
    public static bool IsRetryableStatus(HttpStatusCode statusCode) => IsServerError(statusCode);

    // Classe une ressource invoice (2xx) par ses ÉVÉNEMENTS : un événement d'ÉCHEC prime (prudence
    // fiscale), sinon un événement d'ÉMISSION (fr:201, api:sent ou postérieur), sinon « en cours ».
    // Un code INCONNU reste « en cours » — JAMAIS « émis » par défaut (CLAUDE.md n°3).
    private static PaSendResult FromInvoice(SuperPdpInvoiceResponse invoice, string rawBody)
    {
        var id = invoice.Id!.Value.ToString(CultureInfo.InvariantCulture);
        return StateFromEvents(invoice.Events) switch
        {
            PaSendState.Issued => PaSendResult.Issued(id, rawResponse: rawBody),
            PaSendState.RejectedByPa => PaSendResult.Rejected(FailureErrors(invoice.Events), id, rawBody),
            _ => new PaSendResult
            {
                State = PaSendState.Sending,
                PaDocumentId = id,
                RawResponse = rawBody,
            },
        };
    }

    private static PaSendState StateFromEvents(IReadOnlyList<SuperPdpInvoiceEvent>? events)
    {
        if (events is null || events.Count == 0)
        {
            return PaSendState.Sending;
        }

        var codes = events
            .Select(e => e.StatusCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .ToList();

        if (codes.Any(FailureEventCodes.Contains))
        {
            return PaSendState.RejectedByPa;
        }

        return codes.Any(IssuedEventCodes.Contains) ? PaSendState.Issued : PaSendState.Sending;
    }

    // Les événements d'échec convertis en erreurs neutres : code = status_code (ex. fr:213), message =
    // libellé Super PDP (souvent français), conservé INTACT (F06/DR6, CLAUDE.md n°12).
    private static List<PaError> FailureErrors(IReadOnlyList<SuperPdpInvoiceEvent>? events) =>
        events is null
            ? []
            : events
                .Where(e => e.StatusCode is not null && FailureEventCodes.Contains(e.StatusCode))
                .Select(e => new PaError(e.StatusCode!, e.StatusText ?? "Rejet Super PDP (sans libellé)."))
                .ToList();

    // Le format d'erreur Super PDP {"http_status_code","message"} (✅ confirmé sandbox — F14 §4.1), message
    // INTACT ; repli générique français si le corps est illisible (page HTML d'un proxy, etc.).
    private static List<PaError> ParseErrors(string rawBody, HttpStatusCode statusCode)
    {
        var httpCode = ((int)statusCode).ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            try
            {
                var error = JsonSerializer.Deserialize<SuperPdpError>(rawBody, SuperPdpJson.Options);
                if (!string.IsNullOrWhiteSpace(error?.Message))
                {
                    return [new PaError(httpCode, error!.Message!)];
                }
            }
            catch (JsonException)
            {
                // Corps non-JSON : repli sur le message générique ci-dessous, réponse brute conservée.
            }
        }

        return [new PaError(httpCode, $"Rejet Super PDP (HTTP {(int)statusCode}).")];
    }

    private static List<PaError> AuthErrors(HttpStatusCode statusCode, string rawBody)
    {
        var parsed = ParseErrors(rawBody, statusCode);
        var message =
            $"Erreur d'authentification/configuration Super PDP (HTTP {(int)statusCode}) — re-tentable " +
            $"après correction des identifiants OAuth. Détail : {parsed[0].Message}";
        return [new PaError(((int)statusCode).ToString(CultureInfo.InvariantCulture), message)];
    }

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

    private static bool IsSuccess(HttpStatusCode statusCode) =>
        (int)statusCode is >= 200 and <= 299;

    private static bool IsServerError(HttpStatusCode statusCode) =>
        (int)statusCode is >= 500 and <= 599;

    private static bool IsAuthError(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
