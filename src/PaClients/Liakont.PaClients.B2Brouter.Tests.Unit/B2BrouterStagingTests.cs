namespace Liakont.PaClients.B2Brouter.Tests.Unit;

using System.Globalization;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Suite STAGING (réelle) du plug-in B2Brouter — envois RÉELS vers le compte staging
/// (<c>https://api-staging.b2brouter.net</c>). MANUELLE, exécutée AVANT chaque gate PA, JAMAIS en CI
/// (testing-strategy §8 ; ajouter-un-plugin-pa.md §5) : marquée <c>[Trait("Category","Staging")]</c>,
/// elle est exclue de <c>verify-fast</c>, <c>run-tests</c> ET de la CI par filtre. La clé/API réelle
/// vient de variables d'environnement et n'est JAMAIS committée (CLAUDE.md n°10).
/// <para>
/// COMMENT LANCER : voir <c>docs/architecture/testing-strategy.md §8</c>.
/// </para>
/// <para>
/// Pas de skip DYNAMIQUE : xUnit 2.9 n'expose pas <c>Assert.Skip</c> et CLAUDE.md interdit d'ajouter
/// un package (Xunit.SkippableFact) sans ADR. La suite étant déjà exclue de tout run automatique par
/// son trait, elle ne tourne QUE lancée explicitement par un opérateur ; sans configuration, elle
/// ÉCHOUE LOUD (jamais un skip/faux-vert silencieux — testing-strategy §9) avec un message d'action
/// en français.
/// </para>
/// </summary>
[Trait("Category", "Staging")]
public sealed class B2BrouterStagingTests
{
    /// <summary>Variable d'environnement portant la clé API du compte staging (jamais committée).</summary>
    private const string KeyEnvVar = "B2BROUTER_STAGING_KEY";

    /// <summary>Variable d'environnement portant l'identifiant de compte staging.</summary>
    private const string AccountEnvVar = "B2BROUTER_STAGING_ACCOUNT_ID";

    [Fact]
    public async Task Sends_A_Fixture_Invoice_And_Reads_Back_Its_Status()
    {
        var client = CreateStagingClient();

        // Numéro unique par exécution : le numéro est la clé d'unicité côté B2Brouter (F05 §4.2) — un
        // numéro figé serait rejeté en doublon dès la 2e exécution. Données FICTIVES (aucune donnée
        // client — CLAUDE.md n°7) ; l'opérateur peut adapter la fixture à son compte staging.
        var number = "LIAKONT-STG-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var sent = await client.SendDocumentAsync(B2BrouterTestData.Invoice20(number));

        sent.RawResponse.Should().NotBeNullOrEmpty(
            "tout aller-retour staging conserve la réponse brute pour la piste d'audit (F06/DR6)");
        sent.State.Should().NotBe(
            PaSendState.TechnicalError,
            "un échec technique (réseau / 5xx / 401) révèle un défaut de configuration staging (clé / URL / version d'API), pas un rejet métier du document");

        // Émise (ou en cours) → on relit son statut. Un rejet métier (RejectedByPa) prouve aussi que le
        // câblage est bon (la requête a atteint B2Brouter et la réponse est parsée) : on n'exige donc
        // pas l'émission, seulement l'absence d'échec technique ci-dessus.
        if (!string.IsNullOrWhiteSpace(sent.PaDocumentId))
        {
            var status = await client.GetDocumentStatusAsync(sent.PaDocumentId);
            status.PaDocumentId.Should().Be(sent.PaDocumentId);
            status.RawResponse.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Reads_Tax_Reports_And_Account_Info_Of_The_Staging_Account()
    {
        var client = CreateStagingClient();

        // Lectures seules. La liste des tax reports peut être VIDE (ledger pas encore généré, batch
        // ~02:00) — ce n'est PAS une erreur (F05 §2 ; acceptance PAB03) : on vérifie seulement que
        // l'appel aboutit sans lever (une erreur serveur lèverait HttpRequestException et ferait
        // échouer le test loud).
        var reports = await client.ListTaxReportsAsync();
        reports.Should().NotBeNull();

        var account = await client.GetAccountInfoAsync();
        account.Should().NotBeNull("la lecture des informations de compte aboutit (suivi de consommation — F05 §2)");
    }

    // Construit un B2BrouterClient pointé sur le compte STAGING RÉEL à partir des variables d'env :
    // réplique ce que fait la fabrique en production (URL staging + en-têtes X-B2B-API-Key / version).
    // Échoue LOUD avec un message d'action si la configuration manque (pas de skip silencieux — voir
    // l'en-tête de la classe). RetryPolicy par défaut (5 s / 30 s / 2 min) : comportement réel.
    private static B2BrouterClient CreateStagingClient()
    {
        var apiKey = Environment.GetEnvironmentVariable(KeyEnvVar);
        var accountId = Environment.GetEnvironmentVariable(AccountEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException(
                $"Suite staging B2Brouter : définir {KeyEnvVar} (clé API du compte staging) ET "
                + $"{AccountEnvVar} (identifiant de compte) avant de lancer cette suite. "
                + "Voir docs/architecture/testing-strategy.md §8. "
                + "Cette suite n'est JAMAIS exécutée en CI (Category=Staging exclue par filtre).");
        }

        var http = new HttpClient
        {
            BaseAddress = new Uri(B2BrouterDefaults.StagingBaseUrl),
            Timeout = B2BrouterDefaults.HttpTimeout,
        };
        http.DefaultRequestHeaders.Add(B2BrouterDefaults.ApiKeyHeader, apiKey);
        http.DefaultRequestHeaders.Add(B2BrouterDefaults.ApiVersionHeader, B2BrouterDefaults.MinApiVersion);
        return new B2BrouterClient(http, new B2BrouterClientOptions(accountId));
    }
}
