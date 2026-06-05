namespace Liakont.PaClients.B2Brouter.Tests.Unit;

using System.Net;
using System.Text.Json;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Contract.Tests;

/// <summary>
/// Suite de contrat PA COMMUNE (PAA03) rejouée sur le plug-in B2Brouter via un mock HTTP (acceptance
/// PAB04 ; ajouter-un-plugin-pa.md §4 ; testing-strategy §6). Chaque issue d'un PA (succès / rejet
/// métier 4xx / erreur silencieuse 200+errors[] / 5xx / timeout) est matérialisée par une réponse HTTP
/// SCRIPTÉE (<see cref="RoutedHttpMessageHandler"/>), JAMAIS par un appel réel — les envois réels sont
/// la suite staging séparée (<see cref="B2BrouterStagingTests"/>). La suite hérite vérifie l'invariant
/// central du produit (PAA01) : une capacité absente dégrade en résultat TYPÉ, jamais une exception.
/// La seule responsabilité ici est de TRADUIRE l'issue de contrat en réponse(s) B2Brouter ; les
/// assertions vivent dans <see cref="PaClientContractTests"/>.
/// </summary>
public sealed class B2BrouterPaClientContractTests : PaClientContractTests
{
    // Erreur SILENCIEUSE : HTTP 200 mais errors[] non vide (cas piégeux validé staging, F05 §2 :
    // VATEX manquant sur une ligne 0 %). Le mapper la classe RejectedByPa, jamais « émise » (F05 §4.1).
    private const string SilentErrorBody =
        """{"id":"INV-SILENT","state":"issued","errors":[{"code":"VATEX_MISSING","message":"VATEX requis sur une ligne a 0 % (erreur silencieuse)."}]}""";

    // 5xx transitoire (F05 §4.1). Le corps importe peu : seul le code HTTP pilote la classification
    // « technique re-tentable ».
    private const string ServerErrorBody =
        """{"errors":[{"code":"B2B_5XX","message":"Service B2Brouter indisponible (re-tentable)."}]}""";

    /// <inheritdoc />
    protected override IPaClient CreateClient(PaClientContractSetup setup)
    {
        var handler = BuildHandler(setup);

        // Backoff zéro (NoDelay par défaut de B2BrouterTestData.CreateClient) : la boucle de
        // retry/idempotence s'exerce sans attente réelle. Les capacités du setup pilotent le
        // comportement (null = capacités nominales B2Brouter).
        return B2BrouterTestData.CreateClient(handler, setup.Capabilities);
    }

    // Traduit l'issue de contrat (PaSendOutcome) en réponse(s) HTTP B2Brouter scriptée(s) — le SEUL
    // point qui « sait » comment B2Brouter matérialise chaque famille de F05 §4.1. La file épuisée
    // rejoue sa dernière réponse (RoutedHttpMessageHandler) : deux envois du MÊME numéro raccrochent
    // donc le MÊME identifiant — idempotence par la clé d'unicité du numéro côté PA (F05 §4.2).
    private static RoutedHttpMessageHandler BuildHandler(PaClientContractSetup setup)
    {
        var handler = new RoutedHttpMessageHandler();
        switch (setup.Outcome)
        {
            case PaSendOutcome.Success:
                // 200 + état issued + identifiant attribué par la PA → Issued exploitable (F05 §3).
                handler.OnPost(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
                break;

            case PaSendOutcome.Rejected:
                // 4xx métier porteur d'errors[] (SANS identifiant : un rejet n'émet rien) → RejectedByPa,
                // erreurs remontées intactes (F05 §3). Cas terminal : aucune relecture d'idempotence.
                handler.OnPost(HttpStatusCode.UnprocessableEntity, RejectionBody(setup.RejectionErrors));
                break;

            case PaSendOutcome.SilentError:
                // 200 + errors[] non vide → détecté comme rejet, jamais « émis » (F05 §4.1). Cas terminal.
                handler.OnPost(HttpStatusCode.OK, SilentErrorBody);
                break;

            case PaSendOutcome.TechnicalError:
                // 5xx = transitoire re-tentable (F05 §4.1). Le client tente UNE relecture d'idempotence
                // (GET liste) pour raccrocher une éventuelle facture déjà créée : liste vide → numéro
                // absent → on NE raccroche PAS → TechnicalError re-tentable au prochain run (F05 §4.2).
                handler
                    .OnPost(HttpStatusCode.ServiceUnavailable, ServerErrorBody)
                    .OnListInvoices(HttpStatusCode.OK, B2BrouterTestData.EmptyInvoiceListJson);
                break;

            case PaSendOutcome.Timeout:
                // Timeout HTTP (TaskCanceledException, jeton NON annulé) = transitoire (F05 §4.3). Même
                // relecture d'idempotence non concluante (liste vide) → TechnicalError re-tentable.
                handler
                    .OnPostThrows(new TaskCanceledException("Délai d'attente B2Brouter simulé (contrat)."))
                    .OnListInvoices(HttpStatusCode.OK, B2BrouterTestData.EmptyInvoiceListJson);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(setup),
                    setup.Outcome,
                    "Issue de contrat PA non gérée par le mock B2Brouter.");
        }

        return handler;
    }

    // Corps d'un rejet métier B2Brouter : { "errors": [ { "code", "message" } ] }, SANS identifiant
    // (un rejet n'émet rien — F05 §3). Les erreurs du setup remontent intactes ; à défaut, une erreur
    // de contrat générique (la suite n'asserte que « errors[] non vide »).
    private static string RejectionBody(IReadOnlyList<PaError>? errors)
    {
        var source = errors is { Count: > 0 }
            ? errors
            : new[] { new PaError("CT_REJECT", "Rejet métier simulé pour le contrat B2Brouter.") };
        var wire = new { errors = source.Select(e => new { code = e.Code, message = e.Message }) };
        return JsonSerializer.Serialize(wire);
    }
}
