namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;
using Xunit;
using Xunit.Abstractions;

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

    private readonly ITestOutputHelper _output;

    /// <summary>Capture la sortie de test : ids serveur des lignes sandbox créées (visibilité recette).</summary>
    /// <param name="output">Collecteur de sortie xUnit.</param>
    public SuperPdpSandboxTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Sends_A_Pivot_Invoice_For_Real_And_It_Is_Accepted()
    {
        var (client, http, tokenProvider) = CreateSandboxClient();
        var document = await BuildPivotFromSandboxCompaniesAsync(http, tokenProvider);

        await SendAndAssertAcceptedAsync(client, document);
    }

    [Fact]
    public async Task Sends_An_Unpaid_Pivot_With_Due_Date_For_Real_And_It_Is_Accepted()
    {
        // EXT01 / GATE_PIVOT_DUEDATE : la preuve d'envoi RÉEL qui lève BR-CO-25 — une facture à montant
        // dû POSITIF (NON soldée) PORTANT une date d'échéance (BT-9). Sans BT-9, le converter la rejetterait
        // par BR-CO-25 ; avec, elle doit être acceptée et suivre un cycle de vie sain (F14 §3.2/O11).
        var (client, http, tokenProvider) = CreateSandboxClient();
        var document = await BuildPivotFromSandboxCompaniesAsync(
            http, tokenProvider, paymentDueDate: DateTime.UtcNow.Date.AddDays(30));

        await SendAndAssertAcceptedAsync(client, document);
    }

    [Fact]
    public async Task Posts_A_Margin_b2c_transaction_For_Real_And_Server_Assigns_Id()
    {
        // RECETTE e-reporting B2C de la MARGE (enchères, flux 10.3 — B2C09c). Preuve d'envoi RÉEL d'une
        // transaction TMA1 / rôle SE vers POST /v1.beta/b2c_transactions (forme ancrée F03 §2.5/§2.6).
        // ⚠️ Crée une VRAIE ligne en sandbox : l'API n'expose AUCUN DELETE ni clé d'idempotence (2 POST =
        // 2 lignes — ✅ constaté 2026-06-22, id 585/586). La suite Sandbox n'est JAMAIS lancée en CI
        // (Category=Sandbox exclue par filtre — testing-strategy §8). EXIGE le SUCCÈS (jamais RejectedByPa) :
        // un test qui n'exclut que l'erreur technique laisserait passer un payload rejeté (leçon gate 12/06).
        var (client, _, _) = CreateSandboxClient();

        var sent = await client.SendB2cTransactionAsync(MarginTransactionFixture());

        _output.WriteLine(
            $"Transaction B2C marge créée — id serveur = {sent.PaDocumentId} ; état = {sent.State} ; réponse = {sent.RawResponse}");

        sent.Errors.Should().BeEmpty(
            "un envoi B2C sandbox réussi ne porte aucune erreur (message Super PDP : {0})",
            string.Join(" | ", sent.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        sent.State.Should().Be(
            PaSendState.Issued,
            "le POST b2c_transactions CRÉE la transaction → succès terminal ; l'agrégation PPF par période se fait côté serveur (GET /ereportings reste vide juste après le POST)");
        sent.PaDocumentId.Should().NotBeNullOrWhiteSpace("la transaction créée porte l'id serveur assigné par Super PDP (ex. 585)");
        sent.RawResponse.Should().NotBeNullOrEmpty();
    }

    // Envoi RÉEL + relecture du cycle de vie, avec les assertions de SUCCÈS EXIGÉ (critère de gate, F14 §8) :
    // la facture est créée (identifiant attribué), part en file (Sending) ou est déjà émise (Issued si fr:201
    // a été ultra-rapide), et le cycle relu (polling réel, F14 §3.4) ne porte aucun événement d'échec. Un
    // rejet — local ou PA — est un ÉCHEC de la suite.
    private static async Task SendAndAssertAcceptedAsync(SuperPdpClient client, PivotDocumentDto document)
    {
        var sent = await client.SendDocumentAsync(document);

        sent.RawResponse.Should().NotBeNullOrEmpty();
        sent.Errors.Should().BeEmpty(
            "un envoi sandbox réussi ne porte aucune erreur (critère de gate — message Super PDP : {0})",
            string.Join(" | ", sent.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        sent.State.Should().BeOneOf(
            [PaSendState.Sending, PaSendState.Issued],
            "la facture doit être créée et en cours d'envoi (asynchrone) ou émise — jamais rejetée");
        sent.PaDocumentId.Should().NotBeNullOrWhiteSpace("la création aboutie porte l'identifiant attribué par la PA");

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

    // Fixture marge minimale et COHÉRENTE (Σ sous-totaux = totaux — la réconciliation du builder l'exige) :
    // marge TTC 120 € à 20 % → HT 100,00 / TVA 20,00 (TTC = HT + TVA). Un seul taux → un seul sous-total,
    // la forme ancrée F03 §2.5 (TMA1 / rôle SE / decimal half-up). Date du jour (UTC), devise EUR.
    private static B2cReportingTransaction MarginTransactionFixture() => new()
    {
        Category = EReportingTransactionCategory.Tma1,
        Role = EReportingDeclarantRole.Seller,
        CurrencyCode = "EUR",
        Date = DateOnly.FromDateTime(DateTime.UtcNow.Date),
        TaxExclusiveAmount = 100.00m,
        TaxTotal = 20.00m,
        Subtotals = [new B2cReportingTransactionSubtotal { TaxPercent = 20.0m, TaxableAmount = 100.00m, TaxTotal = 20.00m }],
    };

    // Construit le pivot fixture depuis les companies sandbox RÉELLES : le vendeur DOIT être l'entreprise
    // du compte (contrôle serveur — F14 §3.2) et l'acheteur doit être adressable dans l'annuaire sandbox.
    // Vendeur : GET /v1.beta/companies/me. Acheteur : extrait d'une facture de test générée par la sandbox
    // (GET /v1.beta/invoices/generate_test_invoice?format=en16931 — « the buyer will be randomly picked
    // among the other sandbox companies »). Rien n'est codé en dur : les companies sandbox peuvent changer.
    private static async Task<PivotDocumentDto> BuildPivotFromSandboxCompaniesAsync(
        HttpClient http, SuperPdpTokenProvider tokenProvider, DateTime? paymentDueDate = null)
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

        // Numéro unique par exécution (clé d'idempotence external_id — F14 §4.1, anti-doublon). Deux cas :
        //  - SANS échéance (paymentDueDate null) → facture SOLDÉE (acompte BT-113 = TTC → montant dû
        //    BT-115 = 0), cas métier dominant aux enchères (paiement comptant) ;
        //  - AVEC échéance (BT-9 portée — EXT01) → facture NON SOLDÉE (aucun acompte, montant dû positif) :
        //    la date d'échéance satisfait BR-CO-25 (F14 §3.2/O11), critère de GATE_PIVOT_DUEDATE.
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
            prepaidAmount: paymentDueDate is null ? 120m : null,
            paymentDueDate: paymentDueDate);
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
