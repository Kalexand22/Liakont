namespace Liakont.PaClients.ChorusPro.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Couvre la fabrique du plug-in Chorus Pro (CP02) : clé de registre, mode d'authentification (double
/// auth PISTE + compte technique), construction du client squelette, et exercice de la frontière resolver.
/// </summary>
public sealed class ChorusProClientFactoryTests
{
    [Fact]
    public void PaType_Is_The_Registry_Key()
    {
        var factory = new ChorusProClientFactory(new StubChorusProAccountResolver());

        factory.PaType.Should().Be("ChorusPro");
    }

    [Fact]
    public void AuthMode_Is_OAuth2_With_Technical_Account_So_The_Console_Presents_Both_Credential_Sets()
    {
        // F18 §2 : Chorus Pro exige le client_id/client_secret PISTE ET le compte technique cpro-account.
        // La console lit ce mode via le registre pour présenter les bons champs (jamais if (pa is ChorusPro)).
        var factory = new ChorusProClientFactory(new StubChorusProAccountResolver());

        factory.AuthMode.Should().Be(PaAuthMode.OAuth2WithTechnicalAccount);
    }

    [Fact]
    public void Create_Builds_A_ChorusProClient_With_The_Declared_Skeleton_Capabilities()
    {
        var factory = new ChorusProClientFactory(new StubChorusProAccountResolver());

        var client = factory.Create(new PaAccountDescriptor("ChorusPro", "tenant-a"));

        client.Should().BeOfType<ChorusProClient>();
        client.Capabilities.PaName.Should().Be("Chorus Pro");

        // SQUELETTE CP02 : toutes les capacités sont false (rien n'est encore implémenté — CLAUDE.md n°2/3).
        client.Capabilities.SupportsFacturXTransmission.Should().BeFalse();
        client.Capabilities.SupportsB2cReporting.Should().BeFalse();
    }

    [Fact]
    public void Create_Resolves_The_Account_Through_The_Resolver_Boundary()
    {
        var resolver = new CountingResolver();
        var factory = new ChorusProClientFactory(resolver);

        factory.Create(new PaAccountDescriptor("ChorusPro", "tenant-a"));

        resolver.ResolveCount.Should().Be(1, "la fabrique exerce la frontière resolver (CLAUDE.md n°10)");
    }

    [Fact]
    public void Create_With_Null_Account_Throws()
    {
        var factory = new ChorusProClientFactory(new StubChorusProAccountResolver());

        var act = () => factory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_With_Null_Resolver_Throws()
    {
        var act = () => new ChorusProClientFactory(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private sealed class CountingResolver : IChorusProAccountResolver
    {
        public int ResolveCount { get; private set; }

        public ChorusProAccountConfig Resolve(PaAccountDescriptor account)
        {
            ResolveCount++;
            return StubChorusProAccountResolver.Config;
        }
    }
}
