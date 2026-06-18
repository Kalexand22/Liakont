namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.Transmission.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Couvre l'enregistrement DI du plug-in Super PDP : <c>AddSuperPdpPaClient</c> rend la fabrique
/// découvrable par le registre du module PAR CLÉ (aucun câblage produit spécifique à « SuperPdp » —
/// CLAUDE.md n°6/16), enregistre le client HTTP nommé, et un double appel ne crée pas de doublon. Le Host
/// fournit l'<see cref="ISuperPdpAccountResolver"/> (frontière secret) — ici un stub.
/// </summary>
public sealed class SuperPdpRegistrationTests
{
    private static readonly SuperPdpAccountConfig Config =
        new(SuperPdpEnvironment.Sandbox, "ACC-1", "client-FICTIF", "secret-FICTIF");

    [Fact]
    public void AddSuperPdpPaClient_Registers_A_Factory_Resolvable_By_The_Module_Registry()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IPaClientRegistry>();

        registry.IsRegistered("SuperPdp").Should().BeTrue();
        registry.Resolve(new PaAccountDescriptor("SuperPdp", "tenant-a")).Should().BeOfType<SuperPdpClient>();
    }

    [Fact]
    public void Registry_Reports_SuperPdp_As_An_OAuth2_Type()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IPaClientRegistry>();

        // La console lit ce mode pour présenter client_id/client_secret (slice 4) — jamais if (type==SuperPdp).
        registry.DescribeAuthModes()["SuperPdp"].Should().Be(PaAuthMode.OAuth2ClientCredentials);
    }

    [Fact]
    public void AddSuperPdpPaClient_Registers_The_Named_Http_Client()
    {
        using var provider = BuildProvider();

        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

        httpClientFactory.CreateClient(SuperPdpDefaults.HttpClientName).Should().NotBeNull();
    }

    [Fact]
    public void AddSuperPdpPaClient_Called_Twice_Does_Not_Register_A_Duplicate()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISuperPdpAccountResolver>(new StubAccountResolver(Config));
        services.AddSuperPdpPaClient();
        services.AddSuperPdpPaClient();
        services.AddTransmissionModule();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPaClientRegistry>();

        registry.RegisteredTypes.Should().ContainSingle().Which.Should().Be("SuperPdp");
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISuperPdpAccountResolver>(new StubAccountResolver(Config));
        services.AddSuperPdpPaClient();
        services.AddTransmissionModule();
        return services.BuildServiceProvider();
    }
}
