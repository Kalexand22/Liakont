namespace Liakont.PaClients.Fake.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.Transmission.Infrastructure;
using Xunit;

/// <summary>
/// Couvre la fabrique du plug-in factice (acceptance PAA02 : « se référence, se configure et
/// s'enregistre exactement comme B2Brouter / Super PDP ») : clé de type stable, propagation de la
/// configuration aux clients créés, et résolution par le registre de TYPES du module (par clé, jamais
/// un <c>if (type == …)</c> — CLAUDE.md n°6/16).
/// </summary>
public sealed class FakePaClientFactoryTests
{
    [Fact]
    public void PaType_Is_The_Fake_Key()
    {
        new FakePaClientFactory().PaType.Should().Be("Fake");
        FakePaClientFactory.PaTypeKey.Should().Be("Fake");
    }

    [Fact]
    public async Task Create_Produces_A_Client_With_The_Configured_Capabilities()
    {
        var caps = new PaCapabilities { PaName = "FakeConfig", SupportsCreditNotes = false };
        var factory = new FakePaClientFactory(new FakePaClientOptions { Capabilities = caps });

        var client = factory.Create(new PaAccountDescriptor("Fake", "tenant-a"));

        client.Capabilities.PaName.Should().Be("FakeConfig");

        // La capacité restreinte se reflète dans le comportement (avoir → résultat typé, jamais d'exception).
        var result = await client.SendDocumentAsync(TestDocuments.CreditNote("A-X"));
        result.State.Should().Be(PaSendState.CapabilityNotSupported);
    }

    [Fact]
    public void Create_With_A_Null_Account_Throws()
    {
        var factory = new FakePaClientFactory();

        var act = () => factory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Registry_Resolves_The_Fake_Plugin_By_Key_Case_Insensitively()
    {
        var registry = new PaClientRegistry([new FakePaClientFactory()]);

        var client = registry.Resolve(new PaAccountDescriptor("fake", "tenant-a"));

        client.Should().BeOfType<FakePaClient>();
    }
}
