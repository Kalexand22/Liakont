namespace Liakont.Host.Tests.Unit.Startup;

using System.Collections.Generic;
using FluentAssertions;
using Liakont.Host.Startup;
using Liakont.Modules.Signature.Contracts;
using Liakont.Modules.Signature.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Verrouille le câblage DI du module Signature et la logique de validation au démarrage (SIG03, ADR-0027 §4).
/// Calqué sur <see cref="PaClientBootstrapTests"/> : teste le composition root isolément, sans WebApplication.
/// </summary>
public sealed class SignatureBootstrapTests
{
    [Fact]
    public void AddSignatureModule_RegistersRegistry_WithBuiltInOnSiteProvider()
    {
        var services = new ServiceCollection();
        services.AddSignatureModule();

        using var provider = services.BuildServiceProvider();

        var registry = provider.GetService<ISignatureProviderRegistry>();
        registry.Should().NotBeNull("AddSignatureModule doit enregistrer ISignatureProviderRegistry.");

        // SIG08 (ADR-0030) : le plug-in SUR PLACE Wacom est livré PAR le module (frontière Contracts, jamais
        // un if (type == "Wacom")). Le distant Yousign (SIG07) reste câblé séparément au composition root.
        registry!.RegisteredTypes.Should().Contain("Wacom", "le plug-in sur place est intégré au module Signature (SIG08).");
    }

    [Fact]
    public void ValidateSignatureProviderConfiguration_EmptyConfig_DoesNotThrow()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSignatureModule();
        using var provider = services.BuildServiceProvider();

        var act = () => AppBootstrap.ValidateSignatureProviderConfiguration(configuration, provider);

        act.Should().NotThrow("un tenant Recorded démarre sans aucun fournisseur configuré — l'absence n'est jamais une erreur (INV-SIGPROV-6).");
    }

    [Fact]
    public void ValidateSignatureProviderConfiguration_ConfiguredButUnwired_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Signature:EnabledProviders:0"] = "Yousign",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSignatureModule();
        using var provider = services.BuildServiceProvider();

        var act = () => AppBootstrap.ValidateSignatureProviderConfiguration(configuration, provider);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Yousign*",
                "le validateur doit mentionner le type configuré non câblé dans le message opérateur.");
    }
}
