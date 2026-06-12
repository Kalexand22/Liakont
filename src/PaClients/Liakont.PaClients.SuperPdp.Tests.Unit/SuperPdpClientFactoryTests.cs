namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Net;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Couvre la fabrique du plug-in Super PDP : clé de registre, construction du client, et CÂBLAGE OAuth de
/// bout en bout (échange de jeton sur le token-endpoint PUIS émission avec le bearer obtenu) — preuve que
/// la fabrique relie correctement le fournisseur de jeton RÉEL, l'URL de base et l'émission.
/// </summary>
public sealed class SuperPdpClientFactoryTests
{
    private static readonly SuperPdpAccountConfig Config =
        new(SuperPdpEnvironment.Sandbox, "ACC-1", "client-FICTIF", "secret-FICTIF");

    [Fact]
    public void PaType_Is_The_Registry_Key()
    {
        var factory = new SuperPdpClientFactory(
            new FakeHttpClientFactory(StubHttpMessageHandler.Returns(HttpStatusCode.OK, "{}")),
            new StubAccountResolver(Config));

        factory.PaType.Should().Be("SuperPdp");
    }

    [Fact]
    public void Create_Builds_A_SuperPdpClient_With_The_Declared_Capabilities()
    {
        var factory = new SuperPdpClientFactory(
            new FakeHttpClientFactory(StubHttpMessageHandler.Returns(HttpStatusCode.OK, "{}")),
            new StubAccountResolver(Config));

        var client = factory.Create(new PaAccountDescriptor("SuperPdp", "tenant-a"));

        client.Should().BeOfType<SuperPdpClient>();
        client.Capabilities.PaName.Should().Be("Super PDP");
        client.Capabilities.SupportsB2cReporting.Should().BeTrue();
    }

    [Fact]
    public void Create_With_Null_Account_Throws()
    {
        var factory = new SuperPdpClientFactory(
            new FakeHttpClientFactory(StubHttpMessageHandler.Returns(HttpStatusCode.OK, "{}")),
            new StubAccountResolver(Config));

        var act = () => factory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Create_Wires_The_OAuth_Token_Exchange_Then_Sends_With_The_Bearer()
    {
        // /oauth2/token → jeton ; /v1.beta/invoices/convert → XML CII ; /v1.beta/invoices → émission
        // (F14 §3.2). Le client doit obtenir le jeton PUIS dérouler conversion + émission avec
        // « Authorization: Bearer <token> ».
        var handler = new PathRoutingHttpMessageHandler()
            .On("/oauth2/token", HttpStatusCode.OK, """{"access_token":"AT-XYZ","token_type":"bearer","expires_in":1799}""")
            .On("/v1.beta/invoices/convert", HttpStatusCode.OK, SuperPdpTestData.CiiXml)
            .On("/v1.beta/invoices", HttpStatusCode.OK, SuperPdpTestData.IssuedJson);
        var factory = new SuperPdpClientFactory(new FakeHttpClientFactory(handler), new StubAccountResolver(Config));
        var client = factory.Create(new PaAccountDescriptor("SuperPdp", "tenant-a"));

        var result = await client.SendDocumentAsync(SuperPdpTestData.Invoice20());

        result.State.Should().Be(PaSendState.Issued);

        handler.Requests.Should().Contain(r => r.Path.EndsWith("/oauth2/token", StringComparison.Ordinal),
            "la fabrique câble l'échange de jeton OAuth (F14 §3.1)");
        var invoicePost = handler.Requests.Single(r =>
            r.Method == HttpMethod.Post && r.Path.EndsWith("/v1.beta/invoices", StringComparison.Ordinal));
        invoicePost.Authorization!.Scheme.Should().Be("Bearer");
        invoicePost.Authorization.Parameter.Should().Be("AT-XYZ", "l'émission porte le jeton réellement obtenu");
    }
}
