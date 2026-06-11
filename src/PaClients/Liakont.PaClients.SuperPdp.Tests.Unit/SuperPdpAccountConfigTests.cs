namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using FluentAssertions;
using Xunit;

/// <summary>
/// Couvre la configuration de compte Super PDP : sélection de l'URL de base par environnement, caviardage
/// des secrets OAuth dans <see cref="object.ToString"/> (CLAUDE.md n°10), et validation des champs requis
/// (on bloque plutôt que d'envoyer sans authentification — CLAUDE.md n°3).
/// </summary>
public sealed class SuperPdpAccountConfigTests
{
    [Theory]
    [InlineData(SuperPdpEnvironment.Sandbox)]
    [InlineData(SuperPdpEnvironment.Production)]
    public void BaseUrl_Is_Selected_By_Environment(SuperPdpEnvironment environment)
    {
        var config = new SuperPdpAccountConfig(environment, "ACC-1", "client-FICTIF", "secret-FICTIF");

        var expected = environment == SuperPdpEnvironment.Production
            ? SuperPdpDefaults.ProductionBaseUrl
            : SuperPdpDefaults.SandboxBaseUrl;
        config.BaseUrl.Should().Be(new Uri(expected, UriKind.Absolute));
    }

    [Fact]
    public void ToString_Redacts_The_OAuth_Secrets()
    {
        var config = new SuperPdpAccountConfig(SuperPdpEnvironment.Sandbox, "ACC-1", "client-SECRET-id", "secret-VALUE");

        var text = config.ToString();

        text.Should().NotContain("client-SECRET-id", "le client_id ne doit jamais apparaître en clair (CLAUDE.md n°10)");
        text.Should().NotContain("secret-VALUE", "le client_secret ne doit jamais apparaître en clair (CLAUDE.md n°10)");
        text.Should().Contain("***");
        text.Should().Contain("ACC-1", "l'identifiant de compte non sensible reste lisible pour le diagnostic");
    }

    [Theory]
    [InlineData("", "client", "secret")]
    [InlineData("ACC", "", "secret")]
    [InlineData("ACC", "client", "")]
    public void Missing_Required_Field_Throws(string accountId, string clientId, string clientSecret)
    {
        var act = () => new SuperPdpAccountConfig(SuperPdpEnvironment.Sandbox, accountId, clientId, clientSecret);

        act.Should().Throw<ArgumentException>();
    }
}
