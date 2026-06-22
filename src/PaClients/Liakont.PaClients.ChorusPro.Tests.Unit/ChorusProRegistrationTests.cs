namespace Liakont.PaClients.ChorusPro.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.Transmission.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Couvre l'enregistrement DI du plug-in Chorus Pro : <c>AddChorusProPaClient</c> rend la fabrique
/// découvrable par le registre du module PAR CLÉ (aucun câblage produit spécifique à « ChorusPro » —
/// CLAUDE.md n°6/16), enregistre le client HTTP nommé, et un double appel ne crée pas de doublon. Le Host
/// fournit l'<see cref="IChorusProAccountResolver"/> (frontière secret) — ici un stub.
/// </summary>
public sealed class ChorusProRegistrationTests
{
    [Fact]
    public void AddChorusProPaClient_Registers_A_Factory_Resolvable_By_The_Module_Registry()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IPaClientRegistry>();

        registry.IsRegistered("ChorusPro").Should().BeTrue();
        registry.Resolve(new PaAccountDescriptor("ChorusPro", "tenant-a")).Should().BeOfType<ChorusProClient>();
    }

    [Fact]
    public void Registry_Reports_ChorusPro_As_An_OAuth2_With_Technical_Account_Type()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IPaClientRegistry>();

        // La console lit ce mode pour présenter creds PISTE + compte technique — jamais if (type==ChorusPro).
        registry.DescribeAuthModes()["ChorusPro"].Should().Be(PaAuthMode.OAuth2WithTechnicalAccount);
    }

    [Fact]
    public void AddChorusProPaClient_Registers_The_Named_Http_Client()
    {
        using var provider = BuildProvider();

        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

        httpClientFactory.CreateClient(ChorusProDefaults.HttpClientName).Should().NotBeNull();
    }

    [Fact]
    public void AddChorusProPaClient_Called_Twice_Does_Not_Register_A_Duplicate()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChorusProAccountResolver>(new StubChorusProAccountResolver());
        services.AddChorusProPaClient();
        services.AddChorusProPaClient();
        services.AddTransmissionModule();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPaClientRegistry>();

        registry.RegisteredTypes.Should().ContainSingle().Which.Should().Be("ChorusPro");
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChorusProAccountResolver>(new StubChorusProAccountResolver());
        services.AddChorusProPaClient();
        services.AddTransmissionModule();
        return services.BuildServiceProvider();
    }
}
