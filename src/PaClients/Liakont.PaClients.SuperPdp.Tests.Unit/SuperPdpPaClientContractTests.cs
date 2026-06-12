namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Net;
using System.Text.Json;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Contract.Tests;

/// <summary>
/// Suite de contrat PA COMMUNE (PAA03) rejouée sur le plug-in Super PDP via un mock HTTP (acceptance
/// PAS03 ; ajouter-un-plugin-pa.md §4 ; testing-strategy §6 ; F14 §8). Chaque issue d'un PA (succès /
/// rejet métier 4xx / échec asynchrone signalé par les <c>events[]</c> / 5xx / timeout) est matérialisée
/// par des réponses HTTP SCRIPTÉES (<see cref="RoutedHttpMessageHandler"/> : conversion en16931 → CII
/// PUIS émission — le chemin réel ✅ confirmé sandbox 2026-06-12, F14 §3.2), JAMAIS par un appel réel —
/// les envois réels sont la suite sandbox séparée (<see cref="SuperPdpSandboxTests"/>,
/// <c>Category=Sandbox</c>). La suite héritée vérifie l'invariant central du produit (PAA01) : une
/// capacité absente dégrade en résultat TYPÉ, jamais une exception — déterminant pour Super PDP qui ne
/// déclare en V1 que <c>SupportsB2cReporting</c> (F14 §5 : avoirs, flux paiement, tax reports,
/// téléchargement et rectification restent <c>false</c>). La seule responsabilité ici est de TRADUIRE
/// l'issue de contrat en réponse(s) Super PDP ; les assertions vivent dans <see cref="PaClientContractTests"/>.
/// </summary>
public sealed class SuperPdpPaClientContractTests : PaClientContractTests
{
    // Échec ASYNCHRONE malgré le succès transport : HTTP 200 mais un event api:invalid dans la ressource
    // (l'équivalent réel de « l'erreur silencieuse » — F14 §4.1, O6 levé). Classé RejectedByPa, jamais « émise ».
    private const string AsyncFailureBody =
        """{"id":3001,"direction":"out","external_id":"CT-3","events":[{"status_code":"api:uploaded","status_text":"Téléversée"},{"status_code":"api:invalid","status_text":"Document invalide avant transmission."}]}""";

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
    // « sait » comment Super PDP matérialise chaque famille de F14 §4.1. Toute issue passe d'abord par la
    // CONVERSION (200 = XML CII) : l'issue s'exprime à l'ÉMISSION. La file épuisée rejoue sa dernière réponse
    // (RoutedHttpMessageHandler) : deux envois du MÊME numéro raccrochent donc le MÊME identifiant —
    // idempotence par external_id (F14 §4.1).
    private static RoutedHttpMessageHandler BuildHandler(PaClientContractSetup setup)
    {
        var handler = new RoutedHttpMessageHandler();
        handler.OnConvert(HttpStatusCode.OK, SuperPdpTestData.CiiXml);
        switch (setup.Outcome)
        {
            case PaSendOutcome.Success:
                // 200 + ressource avec event fr:201 « Émise par la plateforme » → Issued exploitable (F14 §4.1).
                handler.OnPost(HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
                break;

            case PaSendOutcome.Rejected:
                // 4xx métier {"http_status_code","message"} (SANS identifiant : un rejet n'émet rien) →
                // RejectedByPa, message intact (F14 §4.1). Le client vérifie d'abord le refus anti-doublon
                // (relecture par external_id) : liste vide → le rejet est rendu tel quel.
                handler
                    .OnPost(HttpStatusCode.BadRequest, RejectionBody(setup.RejectionErrors))
                    .OnListInvoices(HttpStatusCode.OK, SuperPdpTestData.EmptyInvoiceListJson);
                break;

            case PaSendOutcome.SilentError:
                // 200 transport mais un event api:invalid → détecté comme rejet, jamais « émis » (F14 §4.1).
                handler.OnPost(HttpStatusCode.OK, AsyncFailureBody);
                break;

            case PaSendOutcome.TechnicalError:
                // 5xx = transitoire re-tentable (F14 §4.1). Le client tente UNE relecture d'idempotence
                // (GET liste) pour raccrocher une éventuelle facture déjà créée : liste vide → external_id
                // absent → on NE raccroche PAS → TechnicalError re-tentable au prochain run.
                handler
                    .OnPost(HttpStatusCode.ServiceUnavailable, SuperPdpTestData.ErrorJson(503, "Service Super PDP indisponible (re-tentable)."))
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

    // Corps d'un rejet métier Super PDP : {"http_status_code","message"} (✅ format réel — F14 §4.1), SANS
    // identifiant (un rejet n'émet rien ; le mapper laisse alors PaDocumentId null). Le premier message du
    // setup remonte intact ; à défaut, un message de contrat générique (la suite n'asserte que « erreurs
    // non vides »).
    private static string RejectionBody(IReadOnlyList<PaError>? errors)
    {
        var message = errors is { Count: > 0 }
            ? errors[0].Message
            : "Rejet métier simulé pour le contrat Super PDP.";
        return JsonSerializer.Serialize(new { http_status_code = 400, message });
    }
}
