namespace Liakont.Modules.Signature.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Signature.Contracts;
using Liakont.Modules.Signature.Infrastructure;
using Liakont.Modules.Signature.Tests.Unit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Couvre la résolution des plug-ins de signature par REGISTRE DE TYPES (ADR-0027 §4) : résolution par
/// clé uniquement (jamais de <c>if (type == …)</c>), type DEMANDÉ inconnu = erreur de configuration
/// bloquante (jamais <c>null</c>), registre vide valide, enregistrement DI fonctionnel.
/// </summary>
public sealed class SignatureProviderRegistryTests
{
    [Fact]
    public void Resolve_KnownType_ReturnsProviderFromFactory()
    {
        var factory = new FakeSignatureProviderFactory("Yousign");
        var registry = new SignatureProviderRegistry([factory]);
        var account = new SignatureProviderAccount("Yousign", "tenant-a");

        var provider = registry.Resolve(account);

        provider.Should().NotBeNull();
        factory.LastAccount.Should().BeSameAs(account, "le compte est propagé à la fabrique");
    }

    [Fact]
    public void Resolve_IsCaseInsensitiveOnProviderType()
    {
        var registry = new SignatureProviderRegistry([new FakeSignatureProviderFactory("Yousign")]);

        var provider = registry.Resolve(new SignatureProviderAccount("yousign", "tenant-a"));

        provider.Should().NotBeNull();
    }

    [Fact]
    public void Resolve_UnknownType_Throws_WithFrenchMessage_NeverReturnsNull()
    {
        var registry = new SignatureProviderRegistry([new FakeSignatureProviderFactory("Yousign")]);

        var act = () => registry.Resolve(new SignatureProviderAccount("Inconnu", "tenant-a"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Inconnu*")
            .WithMessage("*plug-in*")
            .Which.Message.Should().Contain("Yousign", "le message liste les plug-ins disponibles");
    }

    [Fact]
    public void Constructor_DuplicateType_Throws()
    {
        var act = () => new SignatureProviderRegistry(
            [new FakeSignatureProviderFactory("Yousign"), new FakeSignatureProviderFactory("yousign")]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Yousign*");
    }

    [Fact]
    public void IsRegistered_And_RegisteredTypes_ReflectFactories()
    {
        var registry = new SignatureProviderRegistry(
            [new FakeSignatureProviderFactory("Yousign"), new FakeSignatureProviderFactory("Wacom")]);

        registry.IsRegistered("yousign").Should().BeTrue();
        registry.IsRegistered("absent").Should().BeFalse();
        registry.RegisteredTypes.Should().BeEquivalentTo(["Yousign", "Wacom"]);
    }

    [Fact]
    public void EmptyRegistry_IsValid_Resolve_Throws_AucunDisponible()
    {
        // La signature est optionnelle : un registre VIDE est un état valide (il se construit sans lever).
        var registry = new SignatureProviderRegistry([]);

        registry.RegisteredTypes.Should().BeEmpty();
        var act = () => registry.Resolve(new SignatureProviderAccount("Yousign", "tenant-a"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*aucun*");
    }

    [Fact]
    public void AddSignatureModule_RegistersRegistry_ThatResolvesRegisteredFactories()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISignatureProviderFactory>(new FakeSignatureProviderFactory("Yousign"));
        services.AddSignatureModule();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ISignatureProviderRegistry>();

        registry.Resolve(new SignatureProviderAccount("Yousign", "tenant-a")).Should().NotBeNull();
    }

    [Fact]
    public void AddSignatureModule_RegistersBuiltInOnSiteWacomProvider()
    {
        // SIG08 (ADR-0030) : le module livre le plug-in SUR PLACE Wacom intégré. AddSignatureModule l'enregistre
        // (frontière Contracts, jamais un if (type == "Wacom")) ; il est résolu en fournisseur OnSite SES.
        var services = new ServiceCollection();
        services.AddSignatureModule();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ISignatureProviderRegistry>();

        registry.RegisteredTypes.Should().Contain("Wacom");
        var wacom = registry.Resolve(new SignatureProviderAccount("Wacom", "tenant-a"));
        wacom.Capabilities.Mode.Should().Be(SignatureMode.OnSite);
        wacom.Capabilities.Supports(SignatureLevel.SES).Should().BeTrue();
        wacom.Capabilities.SupportsBiometricTemplateMatching.Should().BeFalse();
    }
}
