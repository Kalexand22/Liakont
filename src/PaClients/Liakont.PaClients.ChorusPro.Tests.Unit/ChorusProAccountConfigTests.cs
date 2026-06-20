namespace Liakont.PaClients.ChorusPro.Tests.Unit;

using FluentAssertions;
using Xunit;

/// <summary>
/// Vérifie que <see cref="ChorusProAccountConfig"/> normalise <c>BaseUrl</c> avec un slash terminal
/// (RFC 3986 : la résolution relative sans trailing slash écraserait le dernier segment du chemin — F18 §3.3).
/// </summary>
public sealed class ChorusProAccountConfigTests
{
    [Fact]
    public void BaseUrl_Without_Trailing_Slash_Is_Normalised_To_End_With_Slash()
    {
        // Arrange — BaseUrl sans slash terminal : sans normalisation, new Uri(base, "factures/v1/deposer")
        // produirait https://api.piste.gouv.fr/factures/v1/deposer (perd le segment /cpro — RFC 3986).
        var config = new ChorusProAccountConfig(
            environment: ChorusProEnvironment.Qualification,
            baseUrl: new Uri("https://api.piste.gouv.fr/cpro"),
            tokenEndpoint: new Uri("https://oauth.piste.gouv.fr/api/oauth/token"),
            accountId: "CPRO-TEST-ACCOUNT",
            pisteClientId: "piste-client-id-fictif",
            pisteClientSecret: "piste-client-secret-fictif",
            technicalLogin: "login-technique-fictif",
            technicalPassword: "mdp-technique-fictif",
            connectionEmail: "technique@example.invalid");

        // Assert — le chemin de base doit se terminer par /cpro/ pour que la résolution relative
        // produise bien https://api.piste.gouv.fr/cpro/factures/v1/deposer.
        config.BaseUrl.AbsoluteUri.Should().EndWith("/cpro/",
            "la résolution relative RFC 3986 doit résoudre SOUS /cpro/, non le remplacer");
    }

    [Fact]
    public void BaseUrl_Already_With_Trailing_Slash_Is_Unchanged()
    {
        var config = new ChorusProAccountConfig(
            environment: ChorusProEnvironment.Qualification,
            baseUrl: new Uri("https://api.piste.gouv.fr/cpro/"),
            tokenEndpoint: new Uri("https://oauth.piste.gouv.fr/api/oauth/token"),
            accountId: "CPRO-TEST-ACCOUNT",
            pisteClientId: "piste-client-id-fictif",
            pisteClientSecret: "piste-client-secret-fictif",
            technicalLogin: "login-technique-fictif",
            technicalPassword: "mdp-technique-fictif",
            connectionEmail: "technique@example.invalid");

        config.BaseUrl.AbsoluteUri.Should().EndWith("/cpro/");
        config.BaseUrl.AbsoluteUri.Should().NotEndWith("//",
            "le slash terminal ne doit pas être doublé si déjà présent");
    }
}
