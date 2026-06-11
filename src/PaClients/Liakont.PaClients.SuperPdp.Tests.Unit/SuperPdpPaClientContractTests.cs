namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Net;
using System.Text.Json;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Contract.Tests;

/// <summary>
/// Suite de contrat PA COMMUNE (PAA03) rejouée sur le plug-in Super PDP via un mock HTTP (acceptance
/// PAS03 ; ajouter-un-plugin-pa.md §4 ; testing-strategy §6 ; F14 §8). Chaque issue d'un PA (succès /
/// rejet métier 4xx / erreur silencieuse 200 + <c>errors[]</c> / 5xx / timeout) est matérialisée par une
/// réponse HTTP SCRIPTÉE (<see cref="RoutedHttpMessageHandler"/>), JAMAIS par un appel réel — les envois
/// réels sont la suite sandbox séparée (<see cref="SuperPdpSandboxTests"/>, <c>Category=Sandbox</c>). La
/// suite héritée vérifie l'invariant central du produit (PAA01) : une capacité absente dégrade en résultat
/// TYPÉ, jamais une exception — déterminant pour Super PDP qui ne déclare en V1 que <c>SupportsB2cReporting</c>
/// (F14 §5 : avoirs, flux paiement, tax reports, téléchargement et rectification restent <c>false</c>). La
/// seule responsabilité ici est de TRADUIRE l'issue de contrat en réponse(s) Super PDP ; les assertions
/// vivent dans <see cref="PaClientContractTests"/>.
/// </summary>
public sealed class SuperPdpPaClientContractTests : PaClientContractTests
{
    // Erreur SILENCIEUSE : HTTP 200 mais errors[] non vide (le piège VATEX vérifié côté B2Brouter, F05 §2 ;
    // O6 pour Super PDP). Le mapper la classe RejectedByPa, jamais « émise » (F14 §4.1, SuperPdpResponseMapper).
    private const string SilentErrorBody =
        """{"id":"INV-SILENT","state":"issued","errors":[{"code":"VATEX_MISSING","message":"VATEX requis sur une ligne a 0 % (erreur silencieuse)."}]}""";

    // 5xx transitoire (F14 §4.1). Le corps importe peu : seul le code HTTP pilote la classification (re-tentable).
    private const string ServerErrorBody =
        """{"errors":[{"code":"SPDP_5XX","message":"Service Super PDP indisponible (re-tentable)."}]}""";

    /// <inheritdoc />
    protected override IPaClient CreateClient(PaClientContractSetup setup)
    {
        var handler = BuildHandler(setup);

        // Backoff zéro (NoDelay par défaut de SuperPdpTestData.CreateClient) : la boucle de retry /
        // relecture d'idempotence s'exerce sans attente réelle. Le jeton OAuth est stubé (StubTokenProvider) —
        // l'aller-retour OAuth réel est couvert ailleurs (SuperPdpTokenProviderTests). Les capacités du setup
        // pilotent le comportement (null = capacités nominales Super PDP : B2C seul).
        return SuperPdpTestData.CreateClient(handler, setup.Capabilities);
    }

    // Traduit l'issue de contrat (PaSendOutcome) en réponse(s) HTTP Super PDP scriptée(s) — le SEUL point qui
    // « sait » comment Super PDP matérialise chaque famille de F14 §4.1. La file épuisée rejoue sa dernière
    // réponse (RoutedHttpMessageHandler) : deux envois du MÊME numéro raccrochent donc le MÊME identifiant —
    // idempotence par la clé d'unicité du numéro côté PA (F14 §4.2).
    private static RoutedHttpMessageHandler BuildHandler(PaClientContractSetup setup)
    {
        var handler = new RoutedHttpMessageHandler();
        switch (setup.Outcome)
        {
            case PaSendOutcome.Success:
                // 200 + état issued + identifiant attribué par la PA → Issued exploitable (F14 §4).
                handler.OnPost(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
                break;

            case PaSendOutcome.Rejected:
                // 4xx métier porteur d'errors[] (SANS identifiant : un rejet n'émet rien) → RejectedByPa,
                // erreurs remontées intactes (F14 §4.1). Cas terminal : aucune relecture d'idempotence.
                handler.OnPost(HttpStatusCode.UnprocessableEntity, RejectionBody(setup.RejectionErrors));
                break;

            case PaSendOutcome.SilentError:
                // 200 + errors[] non vide → détecté comme rejet, jamais « émis » (F14 §4.1). Cas terminal.
                handler.OnPost(HttpStatusCode.OK, SilentErrorBody);
                break;

            case PaSendOutcome.TechnicalError:
                // 5xx = transitoire re-tentable (F14 §4.1). Le client tente UNE relecture d'idempotence
                // (GET liste) pour raccrocher une éventuelle facture déjà créée : liste vide → numéro absent
                // → on NE raccroche PAS → TechnicalError re-tentable au prochain run (F14 §4.2).
                handler
                    .OnPost(HttpStatusCode.ServiceUnavailable, ServerErrorBody)
                    .OnListInvoices(HttpStatusCode.OK, SuperPdpTestData.EmptyInvoiceListJson);
                break;

            case PaSendOutcome.Timeout:
                // Timeout HTTP (TaskCanceledException, jeton NON annulé) = transitoire (F14 §4.1/§4.3). Même
                // relecture d'idempotence non concluante (liste vide) → TechnicalError re-tentable.
                handler
                    .OnPostThrows(new TaskCanceledException("Délai d'attente Super PDP simulé (contrat)."))
                    .OnListInvoices(HttpStatusCode.OK, SuperPdpTestData.EmptyInvoiceListJson);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(setup),
                    setup.Outcome,
                    "Issue de contrat PA non gérée par le mock Super PDP.");
        }

        return handler;
    }

    // Corps d'un rejet métier Super PDP : { "errors": [ { "code", "message" } ] }, SANS identifiant (un rejet
    // n'émet rien — F14 §4.1 ; le mapper laisse alors PaDocumentId null). Les erreurs du setup remontent
    // intactes ; à défaut, une erreur de contrat générique (la suite n'asserte que « errors[] non vide »).
    private static string RejectionBody(IReadOnlyList<PaError>? errors)
    {
        var source = errors is { Count: > 0 }
            ? errors
            : new[] { new PaError("CT_REJECT", "Rejet métier simulé pour le contrat Super PDP.") };
        var wire = new { errors = source.Select(e => new { code = e.Code, message = e.Message }) };
        return JsonSerializer.Serialize(wire);
    }
}
