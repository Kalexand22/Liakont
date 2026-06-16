namespace Liakont.PaClients.Generique.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.Transmission.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Fabrique du plug-in générique + enregistrement DI (ajouter-un-plugin-pa §3) : la résolution se fait par
/// la CLÉ <c>PaType</c> via le registre du module Transmission (jamais un <c>if (pa is …)</c> — CLAUDE.md
/// n°6/8/16), et la fabrique sélectionne le canal de livraison par le mode déclaré du compte.
/// </summary>
public sealed class GeneriqueClientFactoryTests
{
    private static GeneriqueAccountConfig EmailConfig() =>
        new() { Method = DocumentDeliveryMethod.Email, Target = "pa@tenant.test" };

    [Fact]
    public void PaType_Is_The_Generique_Registry_Key()
    {
        var factory = new GeneriqueClientFactory(
            [new RecordingDeliveryChannel(DocumentDeliveryMethod.Email)],
            new StubAccountResolver(EmailConfig()));

        factory.PaType.Should().Be("Generique");
    }

    [Fact]
    public void Create_Selects_The_Channel_Matching_The_Account_Method()
    {
        var factory = new GeneriqueClientFactory(
            [
                new RecordingDeliveryChannel(DocumentDeliveryMethod.FileDeposit),
                new RecordingDeliveryChannel(DocumentDeliveryMethod.Email),
            ],
            new StubAccountResolver(EmailConfig()));

        var client = factory.Create(new PaAccountDescriptor("Generique", "tenant-1"));

        client.Should().BeAssignableTo<IPaClient>();
        client.Capabilities.SupportsFacturXTransmission.Should().BeTrue();
    }

    [Fact]
    public void Create_Throws_When_No_Channel_Matches_The_Account_Method()
    {
        // Compte configuré en dépôt de fichier mais aucun canal correspondant enregistré → blocage clair.
        var factory = new GeneriqueClientFactory(
            [new RecordingDeliveryChannel(DocumentDeliveryMethod.Email)],
            new StubAccountResolver(new GeneriqueAccountConfig
            {
                Method = DocumentDeliveryMethod.FileDeposit,
                Target = "/depot/tenant",
            }));

        var act = () => factory.Create(new PaAccountDescriptor("Generique", "tenant-1"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddGeneriquePaClient_Registers_A_Factory_Resolvable_By_Key()
    {
        var services = new ServiceCollection();
        services.AddTransmissionModule();
        services.AddSingleton<IGeneriqueAccountResolver>(new StubAccountResolver(EmailConfig()));
        services.AddSingleton<IDocumentDeliveryChannel>(new RecordingDeliveryChannel(DocumentDeliveryMethod.Email));
        services.AddGeneriquePaClient();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPaClientRegistry>();

        registry.IsRegistered("Generique").Should().BeTrue();
        var client = registry.Resolve(new PaAccountDescriptor("Generique", "tenant-1"));
        client.Capabilities.SupportsFacturXTransmission.Should().BeTrue();
    }
}
