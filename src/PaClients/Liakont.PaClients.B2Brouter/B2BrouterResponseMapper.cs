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

        // (2) 401/403 = erreur d'AUTHENTIFICATION/configuration (clé invalide ou en rotation), PAS un
        // rejet métier du document (F05 §4.1 : « 401 → bloquer tout le run, pas juste le document »).
        // On la classe re-tentable (TechnicalError) plutôt que RejectedByPa TERMINAL : sous le modèle
        // supersede, un rejet figerait des documents VALIDES qu'il faudrait recréer alors qu'une simple
        // correction de clé suffit. Le code HTTP (401/403) est porté comme code d'erreur → l'auth reste
        // DISTINGUABLE d'un 5xx/réseau. L'escalade run-level (bloquer le run) + l'alerte SUP01 sont du
        // ressort du CONSOMMATEUR (pipeline PIP01) : ni le pipeline ni SUP01 n'existent dans le périmètre
        // PAB02 ; le client se borne à surfacer le signal, pas à orchestrer le run (frontière du plug-in).
        if (IsAuthError(statusCode))
        {
            var authErrors = errors.Count > 0
                ? errors
                : new List<PaError>
                {
                    new(
                        ((int)statusCode).ToString(CultureInfo.InvariantCulture),
                        $"Erreur d'authentification/configuration B2Brouter (HTTP {(int)statusCode}) — re-tentable après correction de la clé."),
                };
            return PaSendResult.Technical(authErrors, rawBody);
        }

        // (3) errors[] non vide (4xx OU 200 silencieux) → rejet, jamais une émission (F05 §4.1).
        if (errors.Count > 0)
        {
            return PaSendResult.Rejected(errors, paDocumentId: parsed?.Id, rawResponse: rawBody);
        }

        // (4) 2xx : l'ÉTAT RÉEL pilote le résultat (F05 §3). Un document « new » (créé sans envoi,
        // NON facturable — F05 §2) ou « sending » (envoi async en cours) n'est PAS « émis » : ne jamais
        // compter émis un document non transmis (correction fiscale/audit — CLAUDE.md n°3).
        if (IsSuccess(statusCode))
        {
            if (string.IsNullOrWhiteSpace(parsed?.Id))
            {
                return PaSendResult.Technical(
                    [new PaError("B2B_NO_ID", "Réponse 2xx sans identifiant ni erreur — émission non confirmée.")],
                    rawBody);
            }

            return MapSuccessState(parsed!, rawBody);
        }

        // (5) 4xx métier sans errors[] (ex. 404, 422 sans corps détaillé) → rejet, pas de retry (F05 §4.1).
        // Les codes d'AUTH (401/403) sont déjà traités en (2) ; ce rejet est document-level.
        var httpCode = (int)statusCode;
        var statusError = new PaError(
            httpCode.ToString(CultureInfo.InvariantCulture),
            $"Rejet B2Brouter (HTTP {httpCode}).");
        return PaSendResult.Rejected([statusError], rawResponse: rawBody);
    }

    /// <summary>
    /// Classe une réponse de RELECTURE d'état (<c>GET /invoices/{id}.json</c>, F05 §3) en
    /// <see cref="PaDocumentStatus"/>, avec la MÊME grille que l'envoi (5xx/auth re-tentables,
    /// <c>errors[]</c> = rejet même sur 200, état réel sinon). Le retry transitoire est piloté par le
    /// client ; ce mapper classe la réponse FINALE obtenue.
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
            // 5xx + 401/403 : re-tentable (réseau/config), jamais un état métier figé (F05 §4.1).
            state = PaSendState.TechnicalError;
        }
        else if (errors.Count > 0)
        {
            // errors[] non vide (4xx OU 200 silencieux) → rejet (F05 §4.1).
            state = PaSendState.RejectedByPa;
        }
        else if (IsSuccess(statusCode))
        {
            state = StateFromString(parsed?.State);
        }
        else
        {
            // 4xx sans errors[] (ex. 404) → rejet document-level, avec le code HTTP comme motif.
            errors =
            [
                new PaError(
                    ((int)statusCode).ToString(CultureInfo.InvariantCulture),
                    $"Relecture B2Brouter en échec (HTTP {(int)statusCode})."),
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
    /// Construit le résultat d'envoi RACCROCHÉ à une facture retrouvée dans la liste du compte lors de
    /// la relecture d'idempotence (F05 §4.2) : on ne re-poste pas, on rattache l'état réel de la facture
    /// déjà créée. Une facture retrouvée AVEC <c>errors[]</c> reste un rejet (pas une émission).
    /// </summary>
    /// <param name="invoice">Facture retrouvée (doit porter un identifiant).</param>
    /// <param name="rawBody">Corps brut de la liste, conservé pour l'audit.</param>
    public static PaSendResult MapReconnected(B2BrouterInvoiceResponse invoice, string rawBody)
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
                [new PaError("B2B_NO_ID", "Facture retrouvée sans identifiant — raccrochage impossible.")],
                rawBody);
        }

        return MapSuccessState(invoice, rawBody);
    }

    /// <summary>
    /// Parse défensivement une liste de factures (relecture d'idempotence, F05 §4.2). Tolère deux
    /// formes : un tableau JSON nu OU l'enveloppe <c>{ "invoices": [...] }</c> (forme exacte à
    /// confirmer en staging — PAB04). Toute forme non reconnue retourne <c>false</c> : le résultat est
    /// alors NON CONCLUANT côté client (on n'en déduit JAMAIS « facture absente » — pas de re-POST à
    /// l'aveugle qui créerait un doublon, CLAUDE.md n°3).
    /// </summary>
    /// <param name="rawBody">Corps brut de la réponse de liste.</param>
    /// <param name="invoices">Les factures parsées si la forme est reconnue ; <c>null</c> sinon.</param>
    /// <returns><c>true</c> si une liste a pu être parsée (même vide), <c>false</c> si non concluant.</returns>
    public static bool TryParseInvoiceList(string rawBody, out IReadOnlyList<B2BrouterInvoiceResponse> invoices)
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
                    invoices = JsonSerializer.Deserialize<List<B2BrouterInvoiceResponse>>(rawBody, B2BrouterJson.Options)
                        ?? [];
                    return true;
                case JsonValueKind.Object:
                    var wrapped = JsonSerializer.Deserialize<B2BrouterInvoiceListResponse>(rawBody, B2BrouterJson.Options);
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

    /// <summary>Vrai si le code HTTP est une erreur TRANSITOIRE re-tentable (5xx — F05 §4.1).</summary>
    public static bool IsRetryableStatus(HttpStatusCode statusCode) => IsServerError(statusCode);

    // Mappe une chaîne d'état B2Brouter (F05 §3) vers l'état neutre. Jamais « émis » par défaut : un
    // état inconnu reste « en cours » (Sending), « error » est un rejet — correction fiscale/audit.
    private static PaSendState StateFromString(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "issued" => PaSendState.Issued,
        "sending" => PaSendState.Sending,
        "new" => PaSendState.New,
        "error" => PaSendState.RejectedByPa,
        _ => PaSendState.Sending,
    };

    // Mappe l'état d'une réponse 2xx SANS errors[] (états F05 §3 : new / sending / issued / error).
    // Seul « issued » est une ÉMISSION ; « new »/« sending » restent intermédiaires (non facturables),
    // « error » est un rejet, et un état inconnu reste « en cours » — JAMAIS « émis » par défaut.
    private static PaSendResult MapSuccessState(B2BrouterInvoiceResponse parsed, string rawBody)
    {
        var id = parsed.Id!;
        var taxReportIds = MapTaxReportIds(parsed.TaxReportIds);
        return parsed.State?.Trim().ToLowerInvariant() switch
        {
            "issued" => PaSendResult.Issued(id, taxReportIds, rawBody),
            "sending" => InProgress(PaSendState.Sending, id, taxReportIds, rawBody),
            "new" => InProgress(PaSendState.New, id, taxReportIds, rawBody),
            "error" => PaSendResult.Rejected(
                [new PaError("B2B_STATE_ERROR", "État B2Brouter « error » sans détail d'erreur.")],
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

    private static bool IsAuthError(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
