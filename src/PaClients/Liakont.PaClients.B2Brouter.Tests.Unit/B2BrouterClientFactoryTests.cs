namespace Liakont.PaClients.B2Brouter.Tests.Unit;

using System.Net;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Tests de la fabrique : résolution par clé (<c>PaType</c>), un client par compte (F05 §4.4) et
/// configuration HTTP du compte — URL de base selon l'environnement (F05 §2) et en-têtes d'auth/version
/// (F05 §2). La clé API résolue n'apparaît que sur l'en-tête HTTP, jamais ailleurs (CLAUDE.md n°10).
/// </summary>
public sealed class B2BrouterClientFactoryTests
{
    private static readonly B2BrouterAccountConfig StagingConfig =
        new(B2BrouterEnvironment.Staging, "ACC-77", "cle-FICTIVE-staging", "2026-03-02");

    [Fact]
    public void PaType_Is_The_Registry_Key()
    {
        var factory = CreateFactory(StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson));

        factory.PaType.Should().Be("B2Brouter");
    }

    [Fact]
    public void Create_Returns_A_B2Brouter_Client()
    {
        var factory = CreateFactory(StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson));

        var client = factory.Create(new PaAccountDescriptor("B2Brouter", "tenant-a"));

        client.Should().BeAssignableTo<IPaClient>().And.BeOfType<B2BrouterClient>();
        client.Capabilities.PaName.Should().Be("B2Brouter");
    }

    [Fact]
    public async Task Create_Configures_Staging_Url_And_Auth_Headers()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
        var factory = CreateFactory(handler, StagingConfig);

        var client = factory.Create(new PaAccountDescriptor("B2Brouter", "tenant-a"));
        await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        handler.LastRequestUri!.Host.Should().Be("api-staging.b2brouter.net", "URL staging (F05 §2)");
        handler.LastRequestUri.AbsolutePath.Should().Be("/accounts/ACC-77/invoices.json");
        handler.LastRequest!.Headers.GetValues("X-B2B-API-Key").Should().ContainSingle().Which.Should().Be("cle-FICTIVE-staging");
        handler.LastRequest.Headers.GetValues("X-B2B-API-Version").Should().ContainSingle().Which.Should().Be("2026-03-02");
    }

    [Fact]
    public async Task Create_Production_Account_Uses_Production_Url()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson);
        var prodConfig = new B2BrouterAccountConfig(B2BrouterEnvironment.Production, "ACC-PROD", "cle-FICTIVE-prod");
        var factory = CreateFactory(handler, prodConfig);

        var client = factory.Create(new PaAccountDescriptor("B2Brouter", "tenant-a"));
        await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        handler.LastRequestUri!.Host.Should().Be("api.b2brouter.net", "URL production (F05 §2)");
    }

    [Fact]
    public void Create_With_Null_Account_Throws()
    {
        var factory = CreateFactory(StubHttpMessageHandler.Returns(HttpStatusCode.OK, B2BrouterTestData.IssuedJson));

        var act = () => factory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static B2BrouterClientFactory CreateFactory(
        StubHttpMessageHandler handler,
        B2BrouterAccountConfig? config = null) =>
        new(new FakeHttpClientFactory(handler), new StubAccountResolver(config ?? StagingConfig));
}
