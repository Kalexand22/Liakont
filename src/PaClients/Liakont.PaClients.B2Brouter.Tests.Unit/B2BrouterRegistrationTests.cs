namespace Liakont.PaClients.B2Brouter.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.Transmission.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Couvre l'enregistrement DI du plug-in B2Brouter : <c>AddB2BrouterPaClient</c> rend la fabrique
/// découvrable par le registre du module PAR CLÉ (aucun câblage produit spécifique à « B2Brouter » —
/// CLAUDE.md n°6/16), enregistre le client HTTP nommé, et un double appel ne crée pas de doublon.
/// Le Host fournit l'<see cref="IB2BrouterAccountResolver"/> (frontière secret) — ici un stub.
/// </summary>
public sealed class B2BrouterRegistrationTests
{
    private static readonly B2BrouterAccountConfig Config =
        new(B2BrouterEnvironment.Staging, "ACC-1", "cle-FICTIVE");

    [Fact]
    public void AddB2BrouterPaClient_Registers_A_Factory_Resolvable_By_The_Module_Registry()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IPaClientRegistry>();

        registry.IsRegistered("B2Brouter").Should().BeTrue();
        registry.Resolve(new PaAccountDescriptor("B2Brouter", "tenant-a")).Should().BeOfType<B2BrouterClient>();
    }

    [Fact]
    public void AddB2BrouterPaClient_Registers_The_Named_Http_Client()
    {
        using var provider = BuildProvider();

        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

        httpClientFactory.CreateClient(B2BrouterDefaults.HttpClientName).Should().NotBeNull();
    }

    [Fact]
    public void AddB2BrouterPaClient_Called_Twice_Does_Not_Register_A_Duplicate()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IB2BrouterAccountResolver>(new StubAccountResolver(Config));
        services.AddB2BrouterPaClient();
        services.AddB2BrouterPaClient();
        services.AddTransmissionModule();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPaClientRegistry>();

        registry.RegisteredTypes.Should().ContainSingle().Which.Should().Be("B2Brouter");
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IB2BrouterAccountResolver>(new StubAccountResolver(Config));
        services.AddB2BrouterPaClient();
        services.AddTransmissionModule();
        return services.BuildServiceProvider();
    }
}
