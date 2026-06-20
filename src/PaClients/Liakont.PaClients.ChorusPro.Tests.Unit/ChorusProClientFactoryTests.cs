namespace Liakont.PaClients.ChorusPro.Tests.Unit;

using System.Net;
using System.Text;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Couvre la fabrique du plug-in Chorus Pro : clé de registre, mode d'authentification (double auth PISTE +
/// compte technique), construction du client, exercice de la frontière resolver, et — CP03 — câblage de la
/// DOUBLE authentification (Bearer PISTE issu de l'échange OAuth2 + en-tête <c>cpro-account</c> dérivé du
/// compte technique).
/// </summary>
public sealed class ChorusProClientFactoryTests
{
    [Fact]
    public void PaType_Is_The_Registry_Key()
    {
        var factory = NewFactory(new RecordingHttpMessageHandler(), new StubChorusProAccountResolver());

        factory.PaType.Should().Be("ChorusPro");
    }

    [Fact]
    public void AuthMode_Is_OAuth2_With_Technical_Account_So_The_Console_Presents_Both_Credential_Sets()
    {
        // F18 §2 : Chorus Pro exige le client_id/client_secret PISTE ET le compte technique cpro-account.
        // La console lit ce mode via le registre pour présenter les bons champs (jamais if (pa is ChorusPro)).
        var factory = NewFactory(new RecordingHttpMessageHandler(), new StubChorusProAccountResolver());

        factory.AuthMode.Should().Be(PaAuthMode.OAuth2WithTechnicalAccount);
    }

    [Fact]
    public void Create_Builds_A_ChorusProClient_With_The_Declared_Skeleton_Capabilities()
    {
        var factory = NewFactory(new RecordingHttpMessageHandler(), new StubChorusProAccountResolver());

        var client = factory.Create(new PaAccountDescriptor("ChorusPro", "tenant-a"));

        client.Should().BeOfType<ChorusProClient>();
        client.Capabilities.PaName.Should().Be("Chorus Pro");

        // Le transport métier (deposerFluxFacture / consulterCR) arrive avec CP04+ : les capacités restent
        // toutes false (CLAUDE.md n°2/3) ; CP03 ne câble que l'authentification.
        client.Capabilities.SupportsFacturXTransmission.Should().BeFalse();
        client.Capabilities.SupportsB2cReporting.Should().BeFalse();
    }

    [Fact]
    public void Create_Resolves_The_Account_Through_The_Resolver_Boundary()
    {
        var resolver = new CountingResolver();
        var factory = NewFactory(new RecordingHttpMessageHandler(), resolver);

        factory.Create(new PaAccountDescriptor("ChorusPro", "tenant-a"));

        resolver.ResolveCount.Should().Be(1, "la fabrique exerce la frontière resolver (CLAUDE.md n°10)");
    }

    [Fact]
    public async Task The_Built_Client_Sends_The_Piste_Bearer_And_The_Derived_Cpro_Account_Header()
    {
        // Câblage de bout en bout : le client construit par la fabrique échange un jeton OAuth2 (1re requête)
        // puis porte sur la requête métier le Bearer PISTE ET l'en-tête cpro-account = base64(login:motDePasse)
        // dérivé du compte technique (F18 §2.2 ; CLAUDE.md n°10 : la valeur est un secret, jamais en clair).
        var handler = new RecordingHttpMessageHandler()
            .Respond(HttpStatusCode.OK, """{"access_token":"AT-WIRED","token_type":"bearer","expires_in":1799}""")
            .Respond(HttpStatusCode.OK);
        var factory = NewFactory(handler, new StubChorusProAccountResolver());

        var client = (ChorusProClient)factory.Create(new PaAccountDescriptor("ChorusPro", "tenant-a"));

        using var response = await client.SendWithAuthAsync(
            () => new HttpRequestMessage(HttpMethod.Post, new Uri("https://sandbox-api.piste.gouv.fr/cpro/x")),
            CancellationToken.None);

        handler.CallCount.Should().Be(2, "1 échange de jeton + 1 requête métier");
        var business = handler.Requests[1];
        business.Authorization!.Scheme.Should().Be("Bearer");
        business.Authorization.Parameter.Should().Be("AT-WIRED");

        var expectedHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes("login-FICTIF:mdp-FICTIF"));
        business.TechnicalAccount.Should().Be(expectedHeader, "cpro-account = base64(login:motDePasse) du compte technique (F18 §2.2)");

        // Le mot de passe en clair ne transite jamais tel quel dans l'en-tête (base64, dérivé une fois).
        business.TechnicalAccount.Should().NotContain("mdp-FICTIF");
    }

    [Fact]
    public void Create_With_Null_Account_Throws()
    {
        var factory = NewFactory(new RecordingHttpMessageHandler(), new StubChorusProAccountResolver());

        var act = () => factory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_With_Null_HttpClientFactory_Throws()
    {
        var act = () => new ChorusProClientFactory(null!, new StubChorusProAccountResolver());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_With_Null_Resolver_Throws()
    {
        var act = () => new ChorusProClientFactory(new FakeHttpClientFactory(new RecordingHttpMessageHandler()), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static ChorusProClientFactory NewFactory(HttpMessageHandler handler, IChorusProAccountResolver resolver) =>
        new(new FakeHttpClientFactory(handler), resolver);

    private sealed class CountingResolver : IChorusProAccountResolver
    {
        public int ResolveCount { get; private set; }

        public ChorusProAccountConfig Resolve(PaAccountDescriptor account)
        {
            ResolveCount++;
            return StubChorusProAccountResolver.Config;
        }
    }
}
