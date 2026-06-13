namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Suite SANDBOX (réelle) du plug-in Super PDP — envois RÉELS vers la sandbox Super PDP
/// (<c>https://api.superpdp.tech</c>). MANUELLE, exécutée AVANT la gate <c>GATE_PA_SUPERPDP</c>, JAMAIS en
/// CI (testing-strategy §8 ; ajouter-un-plugin-pa.md §5 ; F14 §8) : marquée
/// <c>[Trait("Category","Sandbox")]</c>, elle est exclue de <c>verify-fast</c>, <c>run-tests</c>,
/// <c>run-e2e</c> ET de la CI par filtre (<c>Category!=Sandbox</c>). L'authentification est l'OAuth 2.0
/// <c>client_credentials</c> RÉEL (token-endpoint <c>&lt;base&gt;/oauth2/token</c>, F14 §3.1) : les
/// identifiants viennent de variables d'environnement et ne sont JAMAIS committés ni journalisés
/// (CLAUDE.md n°10).
/// <para>
/// La suite EXIGE LE SUCCÈS de l'envoi (facture créée, jamais <c>RejectedByPa</c>) — leçon du faux
/// passage de gate du 2026-06-12 : un test qui n'exclut que l'erreur technique laisse passer un payload
/// rejeté (<c>unknown format</c>) et donc une gate invalide (F14 §8). La fixture pivot est construite
/// depuis les companies sandbox RÉELLES (vendeur = <c>GET /v1.beta/companies/me</c>, acheteur extrait
/// d'une facture <c>generate_test_invoice</c>) : aucun identifiant sandbox codé en dur.
/// </para>
/// </summary>
[Trait("Category", "Sandbox")]
public sealed class SuperPdpSandboxTests
{
    /// <summary>Variable d'environnement portant l'identifiant client OAuth de la sandbox (jamais committée).</summary>
    private const string ClientIdEnvVar = "SUPERPDP_SANDBOX_CLIENT_ID";

    /// <summary>Variable d'environnement portant le secret client OAuth de la sandbox (jamais committée).</summary>
    private const string ClientSecretEnvVar = "SUPERPDP_SANDBOX_CLIENT_SECRET";

    [Fact]
    public async Task Sends_A_Pivot_Invoice_For_Real_And_It_Is_Accepted()
    {
        var (client, http, tokenProvider) = CreateSandboxClient();
        var document = await BuildPivotFromSandboxCompaniesAsync(http, tokenProvider);

        var sent = await client.SendDocumentAsync(document);

        // SUCCÈS EXIGÉ : la facture est créée côté Super PDP (identifiant attribué) et l'envoi part en
        // file (Sending) ou est déjà émis (Issued si le cycle fr:201 a été ultra-rapide). Un rejet — local
        // ou PA — est un ÉCHEC de la suite : c'est le critère de la gate (F14 §8).
        sent.RawResponse.Should().NotBeNullOrEmpty();
        sent.Errors.Should().BeEmpty(
            "un envoi sandbox réussi ne porte aucune erreur (critère de gate — message Super PDP : {0})",
            string.Join(" | ", sent.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        sent.State.Should().BeOneOf(
            [PaSendState.Sending, PaSendState.Issued],
            "la facture doit être créée et en cours d'envoi (asynchrone) ou émise — jamais rejetée");
        sent.PaDocumentId.Should().NotBeNullOrWhiteSpace("la création aboutie porte l'identifiant attribué par la PA");

        // Relecture du cycle de vie par le polling réel (F14 §3.4) : l'état relu doit rester sain
        // (en cours ou émis — un event d'échec apparaîtrait en RejectedByPa).
        var status = await client.GetDocumentStatusAsync(sent.PaDocumentId!);
        status.PaDocumentId.Should().Be(sent.PaDocumentId);
        status.RawResponse.Should().NotBeNullOrEmpty();
        status.State.Should().BeOneOf(
            [PaSendState.Sending, PaSendState.Issued],
            "le cycle de vie relu ne doit porter aucun événement d'échec (api:invalid / fr:213…)");
    }

    // Construit un SuperPdpClient pointé sur la sandbox RÉELLE à partir des variables d'environnement
    // (OAuth client_credentials). Échoue LOUD avec un message d'action si la configuration manque (pas de skip
    // silencieux — testing-strategy §9 : xUnit 2.9 n'a pas de skip dynamique, un [Skip] statique serait un
    // faux-vert). Le token-endpoint est ABSOLU (hors préfixe de version) : construit depuis la base sandbox,
    // comme la fabrique de production (SuperPdpClientFactory).
    private static (SuperPdpClient Client, HttpClient Http, SuperPdpTokenProvider TokenProvider) CreateSandboxClient()
    {
        var clientId = Environment.GetEnvironmentVariable(ClientIdEnvVar);
        var clientSecret = Environment.GetEnvironmentVariable(ClientSecretEnvVar);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                $"Suite sandbox Super PDP : définir {ClientIdEnvVar} (identifiant client OAuth) ET "
                + $"{ClientSecretEnvVar} (secret client OAuth) avant de lancer cette suite. "
                + "Voir docs/architecture/testing-strategy.md §8.2. "
                + "Cette suite n'est JAMAIS exécutée en CI (Category=Sandbox exclue par filtre).");
        }

        var baseUrl = new Uri(SuperPdpDefaults.SandboxBaseUrl);
        var http = new HttpClient { BaseAddress = baseUrl, Timeout = SuperPdpDefaults.HttpTimeout };
        var tokenEndpoint = new Uri(baseUrl, SuperPdpDefaults.TokenPath);
        var tokenProvider = new SuperPdpTokenProvider(http, tokenEndpoint, clientId, clientSecret);
        return (new SuperPdpClient(http, tokenProvider, new SuperPdpClientOptions("SUPERPDP-SANDBOX")), http, tokenProvider);
    }

    // Construit le pivot fixture depuis les companies sandbox RÉELLES : le vendeur DOIT être l'entreprise
    // du compte (contrôle serveur — F14 §3.2) et l'acheteur doit être adressable dans l'annuaire sandbox.
    // Vendeur : GET /v1.beta/companies/me. Acheteur : extrait d'une facture de test générée par la sandbox
    // (GET /v1.beta/invoices/generate_test_invoice?format=en16931 — « the buyer will be randomly picked
    // among the other sandbox companies »). Rien n'est codé en dur : les companies sandbox peuvent changer.
    private static async Task<PivotDocumentDto> BuildPivotFromSandboxCompaniesAsync(
        HttpClient http, SuperPdpTokenProvider tokenProvider)
    {
        var token = await tokenProvider.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        var prefix = SuperPdpDefaults.ApiVersionPrefix;
        using var meDoc = JsonDocument.Parse(await GetAsync(http, token, $"{prefix}/companies/me"));
        var me = meDoc.RootElement;
        var sellerSiren = me.GetProperty("number").GetString();
        var sellerName = me.GetProperty("formal_name").GetString();

        using var testInvoiceDoc = JsonDocument.Parse(
            await GetAsync(http, token, $"{prefix}/{SuperPdpDefaults.InvoicesPath}/generate_test_invoice?format={SuperPdpDefaults.ConvertFromFormat}"));
        var testInvoice = testInvoiceDoc.RootElement;
        var seller = testInvoice.GetProperty("seller");
        var sellerVat = seller.TryGetProperty("vat_identifier", out var vat) ? vat.GetString() : null;
        var buyer = testInvoice.GetProperty("buyer");
        var buyerName = buyer.GetProperty("name").GetString();
        var buyerSiren = buyer.GetProperty("legal_registration_identifier").GetProperty("value").GetString();

        // Numéro unique par exécution (clé d'idempotence external_id — F14 §4.1, anti-doublon). La
        // facture est SOLDÉE (acompte BT-113 = TTC → montant dû BT-115 = 0) : le cas métier dominant aux
        // enchères (paiement comptant), et le pivot ne porte pas encore la date d'échéance (BT-9) exigée
        // par BR-CO-25 quand le montant dû est positif (limitation V1 documentée — F14 §3.2/§12).
        var number = "LIAKONT-SBX-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        return new PivotDocumentDto(
            sourceDocumentKind: "FACTURE",
            number: number,
            issueDate: DateTime.UtcNow.Date,
            sourceReference: $"SRC-{number}",
            supplier: new PivotPartyDto(
                sellerName!,
                siren: sellerSiren,
                vatNumber: sellerVat,
                address: new PivotAddressDto(countryCode: "FR")),
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: new PivotPartyDto(
                buyerName!,
                siren: buyerSiren,
                address: new PivotAddressDto(countryCode: "FR")),
            lines: [new PivotLineDto("Prestation de test Liakont", 100m, taxes: [new PivotLineTaxDto(20m, 20m, VatCategory.S)])],
            prepaidAmount: 120m);
    }

    private static async Task<string> GetAsync(HttpClient http, string bearer, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"GET {url} doit aboutir pour préparer la fixture sandbox (HTTP {(int)response.StatusCode} : {body})");
        return body;
    }
}
