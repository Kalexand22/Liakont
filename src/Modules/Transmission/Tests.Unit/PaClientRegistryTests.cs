namespace Liakont.Modules.Transmission.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.Transmission.Infrastructure;
using Liakont.Modules.Transmission.Tests.Unit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Couvre la résolution des plug-ins PA par REGISTRE DE TYPES (acceptance PAA01 §5 ;
/// INV-TRANSMISSION-003/004) : résolution par clé uniquement (jamais de <c>if (type == …)</c>), type
/// inconnu = erreur de configuration bloquante (jamais <c>null</c>), enregistrement DI fonctionnel.
/// </summary>
public sealed class PaClientRegistryTests
{
    [Fact]
    public void Resolve_KnownType_ReturnsClientFromFactory()
    {
        var factory = new StubPaClientFactory("Fake");
        var registry = new PaClientRegistry([factory]);
        var account = new PaAccountDescriptor("Fake", "tenant-a");

        var client = registry.Resolve(account);

        client.Should().NotBeNull();
        factory.LastAccount.Should().BeSameAs(account, "le compte est propagé à la fabrique");
    }

    [Fact]
    public void Resolve_IsCaseInsensitiveOnPaType()
    {
        var registry = new PaClientRegistry([new StubPaClientFactory("B2Brouter")]);

        var client = registry.Resolve(new PaAccountDescriptor("b2brouter", "tenant-a"));

        client.Should().NotBeNull();
    }

    [Fact]
    public void DescribeAuthModes_Reports_The_Mode_Declared_By_Each_Factory()
    {
        // Option 1 (PAS) : le registre expose le mode d'auth PAR TYPE, sans instancier de client — la console
        // s'en sert pour présenter les bons champs de creds (clé API vs OAuth2). Résolution par clé, jamais
        // un if (type == "SuperPdp") (CLAUDE.md n°8/16).
        var registry = new PaClientRegistry(new IPaClientFactory[]
        {
            new StubPaClientFactory("ClePa"),
            new StubPaClientFactory("OAuthPa", authMode: PaAuthMode.OAuth2ClientCredentials),
        });

        var modes = registry.DescribeAuthModes();

        modes["ClePa"].Should().Be(PaAuthMode.ApiKey);
        modes["OAuthPa"].Should().Be(PaAuthMode.OAuth2ClientCredentials);
        modes["oauthpa"].Should().Be(PaAuthMode.OAuth2ClientCredentials, "la clé de type est insensible à la casse");
    }

    [Fact]
    public void Resolve_UnknownType_Throws_WithFrenchMessage_NeverReturnsNull()
    {
        var registry = new PaClientRegistry([new StubPaClientFactory("Fake")]);

        var act = () => registry.Resolve(new PaAccountDescriptor("Inconnue", "tenant-a"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Inconnue*")
            .WithMessage("*plug-in*")
            .Which.Message.Should().Contain("Fake", "le message liste les plug-ins disponibles");
    }

    [Fact]
    public void Constructor_DuplicateType_Throws()
    {
        var act = () => new PaClientRegistry([new StubPaClientFactory("Fake"), new StubPaClientFactory("fake")]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Fake*");
    }

    [Fact]
    public void IsRegistered_And_RegisteredTypes_ReflectFactories()
    {
        var registry = new PaClientRegistry([new StubPaClientFactory("Fake"), new StubPaClientFactory("B2Brouter")]);

        registry.IsRegistered("fake").Should().BeTrue();
        registry.IsRegistered("absent").Should().BeFalse();
        registry.RegisteredTypes.Should().BeEquivalentTo(["Fake", "B2Brouter"]);
    }

    [Fact]
    public void EmptyRegistry_Resolve_Throws_AucunDisponible()
    {
        var registry = new PaClientRegistry([]);

        var act = () => registry.Resolve(new PaAccountDescriptor("Fake", "tenant-a"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*aucun*");
    }

    [Fact]
    public void AddTransmissionModule_RegistersRegistry_ThatResolvesRegisteredFactories()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPaClientFactory>(new StubPaClientFactory("Fake"));
        services.AddTransmissionModule();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPaClientRegistry>();

        registry.Resolve(new PaAccountDescriptor("Fake", "tenant-a")).Should().NotBeNull();
    }
}
