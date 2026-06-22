namespace Liakont.PaClients.ChorusPro.Tests.Unit;

using System.Globalization;
using System.Text;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Suite SANDBOX (réelle) du plug-in Chorus Pro — dépôt RÉEL d'un Factur-X scellé vers la QUALIFICATION
/// Chorus Pro / PISTE (<c>*.piste.gouv.fr</c>, F18 §7). MANUELLE, exécutée AVANT la gate humaine
/// <c>GATE_CHORUS_PRO</c>, JAMAIS en CI (testing-strategy §8 ; ajouter-un-plugin-pa.md §5 ; F18 §10) :
/// marquée <c>[Trait("Category","Sandbox")]</c>, elle est exclue de <c>verify-fast</c>, <c>run-tests</c>,
/// <c>run-e2e</c> ET de la CI par filtre (<c>Category!=Sandbox</c>, voir tools/run-tests.ps1 +
/// tools/verify-fast.ps1). Modèle technique : <see cref="SuperPdp.Tests.Unit.SuperPdpSandboxTests"/>.
/// <para>
/// La suite EXIGE LE SUCCÈS d'un parcours COMPLET — dépôt accepté → <see cref="PaSendState.Sending"/>
/// (JAMAIS <see cref="PaSendState.Issued"/> au dépôt, A1/D5), puis relecture <c>consulterCR</c> jusqu'à
/// <c>etatCourantFlux=Intégré</c> → <see cref="PaSendState.Issued"/> RÉEL en qualif. Leçon du faux passage
/// de gate du 2026-06-12 (Super PDP) : un test qui n'exclut que l'erreur technique laisse passer un payload
/// rejeté et donc une gate invalide. Ici un <see cref="PaSendState.RejectedByPa"/> à n'importe quelle étape
/// est un ÉCHEC de la suite (F18 §10).
/// </para>
/// <para>
/// Chorus Pro est un transport PUR (niveau « Essentiel », F18 §6) : le plug-in NE génère NI ne scelle le
/// Factur-X — il dépose un PDF/A-3 DÉJÀ scellé fourni par le pipeline (CLAUDE.md n°6). Cette suite ne peut
/// donc pas fabriquer l'artefact (un faux PDF serait rejeté par Chorus Pro = faux-vert) ; l'opérateur
/// fournit un Factur-X de qualification RÉEL via <c>CHORUSPRO_SANDBOX_FACTURX_PATH</c>. Aucun secret, aucune
/// donnée client, aucune URL de qualif n'est codée en dur (CLAUDE.md n°7/10 ; F18 §3.3 « ne pas
/// hardcoder ») : tout vient de variables d'environnement, jamais committées ni journalisées.
/// </para>
/// </summary>
[Trait("Category", "Sandbox")]
public sealed class ChorusProSandboxTests
{
    /// <summary>Base API Chorus Pro de qualif (host + <c>/cpro/</c>, F18 §3.3 — portée par compte, jamais figée dans le code).</summary>
    private const string BaseUrlEnvVar = "CHORUSPRO_SANDBOX_BASE_URL";

    /// <summary>URL absolue du token-endpoint OAuth2 PISTE de qualif (<c>&lt;oauth&gt;/api/oauth/token</c>, F18 §2.1).</summary>
    private const string TokenEndpointEnvVar = "CHORUSPRO_SANDBOX_TOKEN_ENDPOINT";

    /// <summary>Identifiant client OAuth2 PISTE (application SANDBOX) — jamais committé (CLAUDE.md n°10).</summary>
    private const string PisteClientIdEnvVar = "CHORUSPRO_SANDBOX_PISTE_CLIENT_ID";

    /// <summary>Secret client OAuth2 PISTE (application SANDBOX) — jamais committé (CLAUDE.md n°10).</summary>
    private const string PisteClientSecretEnvVar = "CHORUSPRO_SANDBOX_PISTE_CLIENT_SECRET";

    /// <summary>Login du compte technique Chorus Pro de qualif (composant de <c>cpro-account</c>, F18 §2.2).</summary>
    private const string TechnicalLoginEnvVar = "CHORUSPRO_SANDBOX_TECHNICAL_LOGIN";

    /// <summary>Mot de passe du compte technique Chorus Pro de qualif (SECRET — jamais committé, CLAUDE.md n°10).</summary>
    private const string TechnicalPasswordEnvVar = "CHORUSPRO_SANDBOX_TECHNICAL_PASSWORD";

    /// <summary>Chemin d'un Factur-X (PDF/A-3 scellé) de qualif RÉEL à déposer — l'artefact n'est pas fabriqué par la suite.</summary>
    private const string FacturXPathEnvVar = "CHORUSPRO_SANDBOX_FACTURX_PATH";

    /// <summary>Durée maximale (s) d'attente de l'état <c>Intégré</c> à la relecture (l'intégration Chorus Pro est asynchrone, F18 §4).</summary>
    private const string PollSecondsEnvVar = "CHORUSPRO_SANDBOX_POLL_SECONDS";

    /// <summary>Intervalle (s) entre deux relectures <c>consulterCR</c> pendant l'attente de l'intégration.</summary>
    private const string PollIntervalSecondsEnvVar = "CHORUSPRO_SANDBOX_POLL_INTERVAL_SECONDS";

    private const int DefaultPollSeconds = 600;
    private const int DefaultPollIntervalSeconds = 20;

    [Fact]
    public async Task Deposits_A_Sealed_FacturX_For_Real_And_It_Reaches_Issued()
    {
        var (client, http) = CreateSandboxClient();
        using (http)
        {
            var artifact = await LoadFacturXArtifactAsync();
            var document = BuildPivot();

            // Dépôt deposerFluxFacture : accusé de réception → Sending, JAMAIS Issued au dépôt (A1/D5, F18 §3).
            var sent = await client.SendDocumentAsync(document, context: new PaSendContext(artifact));

            sent.Errors.Should().BeEmpty(
                "un dépôt Chorus Pro accepté ne porte aucune erreur (critère de gate — message Chorus Pro : {0})",
                string.Join(" | ", sent.Errors.Select(e => $"[{e.Code}] {e.Message}")));
            sent.State.Should().Be(
                PaSendState.Sending,
                "le dépôt accusé réception est Sending (accusé), jamais Issued au dépôt (A1/D5)");
            sent.PaDocumentId.Should().NotBeNullOrWhiteSpace("le dépôt abouti porte le numeroFluxDepot attribué par Chorus Pro");

            // Relecture consulterCR jusqu'à l'intégration RÉELLE (asynchrone) : Intégré → Issued SEUL (A1/D5).
            // Un Rejeté / Incidenté / Intégré partiellement à n'importe quelle relecture est un ÉCHEC (F18 §10).
            var status = await PollUntilIssuedAsync(client, sent.PaDocumentId!);

            status.PaDocumentId.Should().Be(sent.PaDocumentId);
            status.RawResponse.Should().NotBeNullOrEmpty("le compte rendu brut est conservé pour l'audit (F06/DR6)");
            status.State.Should().Be(
                PaSendState.Issued,
                "le parcours dépôt→Sending→consulterCR=Intégré→Issued doit aboutir RÉELLEMENT en qualif (pas de faux-vert)");
        }
    }

    // Relit consulterCR jusqu'à Issued ou jusqu'au délai imparti. Échoue LOUD sur un rejet PA (état figé) ou
    // sur l'expiration du délai (l'intégration n'a pas abouti) — jamais de skip silencieux. L'erreur technique
    // transitoire (5xx/réseau/timeout) est tolérée et ré-essayée jusqu'au délai (l'intégration est asynchrone).
    private static async Task<PaDocumentStatus> PollUntilIssuedAsync(ChorusProClient client, string fluxId)
    {
        var deadlineSeconds = ReadPositiveInt(PollSecondsEnvVar, DefaultPollSeconds);
        var intervalSeconds = ReadPositiveInt(PollIntervalSecondsEnvVar, DefaultPollIntervalSeconds);

        var elapsed = 0;
        while (true)
        {
            var status = await client.GetDocumentStatusAsync(fluxId);

            status.State.Should().NotBe(
                PaSendState.RejectedByPa,
                "un rejet Chorus Pro (Rejeté / Incidenté / Intégré partiellement) est un ÉCHEC de gate — compte rendu : {0}",
                status.RawResponse);

            if (status.State == PaSendState.Issued)
            {
                return status;
            }

            if (elapsed >= deadlineSeconds)
            {
                status.State.Should().Be(
                    PaSendState.Issued,
                    "l'intégration Chorus Pro n'a pas abouti à Issued en {0} s (état observé : {1} — augmenter {2} si la qualif est lente, compte rendu : {3})",
                    deadlineSeconds,
                    status.State,
                    PollSecondsEnvVar,
                    status.RawResponse);
                return status; // inatteignable : l'assertion ci-dessus lève — présent pour le contrat de retour non-nul.
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
            elapsed += intervalSeconds;
        }
    }

    // Construit un ChorusProClient pointé sur la QUALIF RÉELLE depuis les variables d'environnement : base API +
    // token-endpoint absolus (F18 §3.3 — jamais hardcodés), OAuth2 client_credentials PISTE (scope=openid) et
    // en-tête cpro-account du compte technique (base64(login:motDePasse), F18 §2.2). Échoue LOUD avec un message
    // d'action si la configuration manque (pas de skip silencieux — testing-strategy §9 : xUnit 2.9 n'a pas de
    // skip dynamique, un [Skip] statique serait un faux-vert).
    private static (ChorusProClient Client, HttpClient Http) CreateSandboxClient()
    {
        var baseUrl = ReadAbsoluteUri(BaseUrlEnvVar);
        var tokenEndpoint = ReadAbsoluteUri(TokenEndpointEnvVar);
        var clientId = ReadRequired(PisteClientIdEnvVar);
        var clientSecret = ReadRequired(PisteClientSecretEnvVar);
        var technicalLogin = ReadRequired(TechnicalLoginEnvVar);
        var technicalPassword = ReadRequired(TechnicalPasswordEnvVar);

        var http = new HttpClient { BaseAddress = baseUrl, Timeout = ChorusProDefaults.HttpTimeout };
        var tokenProvider = new ChorusProTokenProvider(
            http, tokenEndpoint, clientId, clientSecret, ChorusProDefaults.TokenScope);

        // cpro-account = base64(login:motDePasse), pré-calculé comme en production (ChorusProClientFactory).
        var technicalAccountHeader = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{technicalLogin}:{technicalPassword}"));

        var client = new ChorusProClient(
            http, tokenProvider, technicalAccountHeader, ChorusProCapabilities.Declared);
        return (client, http);
    }

    // Charge le Factur-X scellé de qualif depuis le chemin fourni (l'artefact n'est jamais fabriqué : un faux
    // PDF serait rejeté = faux-vert). Échoue LOUD si la variable manque ou si le fichier est introuvable/vide.
    private static async Task<byte[]> LoadFacturXArtifactAsync()
    {
        var path = ReadRequired(FacturXPathEnvVar);
        File.Exists(path).Should().BeTrue(
            "le Factur-X de qualif référencé par {0} doit exister (chemin : {1})", FacturXPathEnvVar, path);

        var bytes = await File.ReadAllBytesAsync(path);
        bytes.Should().NotBeEmpty("le Factur-X de qualif déposé ne doit pas être vide (transport pur — F18 §6)");
        return bytes;
    }

    // Pivot minimal pour le dépôt : Chorus Pro est un transport PUR — le plug-in NE LIT NI le contenu métier
    // (montants, parties) NI ne le sérialise dans le payload (seul le numéro alimente le nomFichier, F18 §3.1).
    // Données FICTIVES, montants en decimal (CLAUDE.md n°1/7). Numéro unique par exécution (traçabilité du dépôt).
    private static PivotDocumentDto BuildPivot()
    {
        var number = "LIAKONT-CPRO-SBX-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        return new PivotDocumentDto(
            sourceDocumentKind: "FACTURE",
            number: number,
            issueDate: DateTime.UtcNow.Date,
            sourceReference: $"SRC-{number}",
            supplier: new PivotPartyDto("SVV Démo", siren: "123456789", vatNumber: "FR32123456789"),
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: new PivotPartyDto("Client Démo", siren: "987654321"),
            lines: [new PivotLineDto("Prestation de test Liakont", 100m, taxes: [new PivotLineTaxDto(20m, 20m, VatCategory.S)])]);
    }

    private static string ReadRequired(string envVar)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Suite sandbox Chorus Pro : définir {envVar} avant de lancer cette suite. "
                + "Variables requises : "
                + $"{BaseUrlEnvVar}, {TokenEndpointEnvVar}, {PisteClientIdEnvVar}, {PisteClientSecretEnvVar}, "
                + $"{TechnicalLoginEnvVar}, {TechnicalPasswordEnvVar}, {FacturXPathEnvVar}. "
                + "Voir docs/architecture/testing-strategy.md §8 et orchestration/items/CP.yaml (CP09). "
                + "Cette suite n'est JAMAIS exécutée en CI (Category=Sandbox exclue par filtre).");
        }

        return value;
    }

    private static Uri ReadAbsoluteUri(string envVar)
    {
        var value = ReadRequired(envVar);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"Suite sandbox Chorus Pro : {envVar} doit être une URL absolue (valeur : « {value} »).");
        }

        return uri;
    }

    private static int ReadPositiveInt(string envVar, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }
}
