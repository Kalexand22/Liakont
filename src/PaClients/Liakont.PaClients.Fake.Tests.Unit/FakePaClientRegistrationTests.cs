namespace Liakont.PaClients.Fake.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.Transmission.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Couvre l'enregistrement DI du plug-in factice (acceptance PAA02 : « s'enregistre exactement comme
/// B2Brouter / Super PDP ») : <c>AddFakePaClient</c> rend la fabrique découvrable par le registre du
/// module sans aucun câblage produit spécifique à « Fake », et un double appel ne crée pas de doublon
/// (qui ferait lever le registre pour type dupliqué).
/// </summary>
public sealed class FakePaClientRegistrationTests
{
    [Fact]
    public void AddFakePaClient_Registers_A_Factory_Resolvable_By_The_Module_Registry()
    {
        var services = new ServiceCollection();
        services.AddFakePaClient();
        services.AddTransmissionModule();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPaClientRegistry>();

        registry.IsRegistered("Fake").Should().BeTrue();
        registry.Resolve(new PaAccountDescriptor("Fake", "tenant-a")).Should().BeOfType<FakePaClient>();
    }

    [Fact]
    public void AddFakePaClient_Called_Twice_Does_Not_Register_A_Duplicate()
    {
        var services = new ServiceCollection();
        services.AddFakePaClient();
        services.AddFakePaClient();
        services.AddTransmissionModule();

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IPaClientRegistry>();

        act.Should().NotThrow("la dédup de TryAddEnumerable empêche deux fabriques « Fake »");
        act().RegisteredTypes.Should().ContainSingle().Which.Should().Be("Fake");
    }

    [Fact]
    public void AddFakePaClient_Honours_The_Configured_Options()
    {
        var services = new ServiceCollection();
        var caps = new PaCapabilities { PaName = "FakeConfig", SupportsDomesticPaymentReporting = false };
        services.AddFakePaClient(new FakePaClientOptions { Capabilities = caps });
        services.AddTransmissionModule();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPaClientRegistry>();

        var client = registry.Resolve(new PaAccountDescriptor("Fake", "tenant-a"));
        client.Capabilities.PaName.Should().Be("FakeConfig");
        client.Capabilities.SupportsDomesticPaymentReporting.Should().BeFalse();
    }
}
