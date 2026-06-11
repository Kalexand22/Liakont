namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Globalization;
using FluentAssertions;
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
/// </summary>
[Trait("Category", "Sandbox")]
public sealed class SuperPdpSandboxTests
{
    /// <summary>Variable d'environnement portant l'identifiant client OAuth de la sandbox (jamais committée).</summary>
    private const string ClientIdEnvVar = "SUPERPDP_SANDBOX_CLIENT_ID";

    /// <summary>Variable d'environnement portant le secret client OAuth de la sandbox (jamais committée).</summary>
    private const string ClientSecretEnvVar = "SUPERPDP_SANDBOX_CLIENT_SECRET";

    [Fact]
    public async Task Sends_A_Fixture_Invoice_And_Reads_Back_Its_Status()
    {
        var client = CreateSandboxClient();

        // Numéro unique par exécution : le numéro est la clé d'unicité côté Super PDP (F14 §4.2 — anti-doublon).
        var number = "LIAKONT-SBX-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var sent = await client.SendDocumentAsync(SuperPdpTestData.Invoice20(number));

        // On ne préjuge PAS de l'acceptation fiscale de la fixture (Issued OU RejectedByPa selon le compte
        // sandbox) : on prouve l'aller-retour réel (OAuth + envoi + réponse classée), jamais une erreur
        // technique. La réponse brute est conservée pour l'audit (F06/DR6).
        sent.RawResponse.Should().NotBeNullOrEmpty();
        sent.State.Should().NotBe(
            PaSendState.TechnicalError,
            "un aller-retour sandbox réussi classe une réponse métier, pas une erreur de transport");

        // Émise (ou en cours) → on relit son statut via le polling (F14 §3.4).
        if (!string.IsNullOrWhiteSpace(sent.PaDocumentId))
        {
            var status = await client.GetDocumentStatusAsync(sent.PaDocumentId);
            status.PaDocumentId.Should().Be(sent.PaDocumentId);
            status.RawResponse.Should().NotBeNullOrEmpty();
        }
    }

    // Construit un SuperPdpClient pointé sur la sandbox RÉELLE à partir des variables d'environnement
    // (OAuth client_credentials). Échoue LOUD avec un message d'action si la configuration manque (pas de skip
    // silencieux — testing-strategy §9 : xUnit 2.9 n'a pas de skip dynamique, un [Skip] statique serait un
    // faux-vert). Le token-endpoint est ABSOLU (hors préfixe de version) : construit depuis la base sandbox,
    // comme la fabrique de production (SuperPdpClientFactory).
    private static SuperPdpClient CreateSandboxClient()
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
        return new SuperPdpClient(http, tokenProvider, new SuperPdpClientOptions("SUPERPDP-SANDBOX"));
    }
}
