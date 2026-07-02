namespace Liakont.Host.Tests.Unit.InstanceEmail;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.InstanceEmail;
using Liakont.Modules.FleetSupervision.Application;
using Microsoft.Extensions.Logging;
using Xunit;

/// <summary>
/// Fournisseur de jeton OAuth email (ADR-0039) : POST <c>refresh_token</c> sur le BON endpoint selon le
/// fournisseur, parsing de l'<c>access_token</c>, mise en cache (pas de second appel réseau dans la fenêtre
/// de validité), échec propagé, et AUCUN secret journalisé (CLAUDE.md n°10). Zéro SDK, zéro réseau réel.
/// </summary>
public sealed class HttpEmailOAuthTokenProviderTests
{
    private const string TokenJson = """{"access_token":"at-123","expires_in":3600}""";

    [Fact]
    public async Task Google_Posts_The_Refresh_Grant_To_The_Google_Endpoint()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, TokenJson);
        var provider = NewProvider(handler);

        var token = await provider.GetAccessTokenAsync(Request(EmailProviderKind.GoogleOAuth2));

        token.Should().Be("at-123");
        handler.LastUri.Should().Be(new Uri("https://oauth2.googleapis.com/token"));
        handler.LastBody.Should().Contain("grant_type=refresh_token")
            .And.Contain("client_id=cid")
            .And.Contain("refresh_token=rtoken");
    }

    [Fact]
    public async Task Microsoft_Posts_To_The_Tenant_Specific_Endpoint()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, TokenJson);
        var provider = NewProvider(handler);

        await provider.GetAccessTokenAsync(Request(EmailProviderKind.MicrosoftOAuth2, tenantId: "my-tenant"));

        handler.LastUri.Should().Be(new Uri("https://login.microsoftonline.com/my-tenant/oauth2/v2.0/token"));
    }

    [Fact]
    public async Task Microsoft_Without_Tenant_Uses_Common()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, TokenJson);
        var provider = NewProvider(handler);

        await provider.GetAccessTokenAsync(Request(EmailProviderKind.MicrosoftOAuth2, tenantId: null));

        handler.LastUri.Should().Be(new Uri("https://login.microsoftonline.com/common/oauth2/v2.0/token"));
    }

    [Fact]
    public async Task A_NonSuccess_Response_Throws()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        var provider = NewProvider(handler);

        var act = async () => await provider.GetAccessTokenAsync(Request(EmailProviderKind.GoogleOAuth2));

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task A_Response_Without_Access_Token_Throws()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"expires_in":3600}""");
        var provider = NewProvider(handler);

        var act = async () => await provider.GetAccessTokenAsync(Request(EmailProviderKind.GoogleOAuth2));

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task The_Token_Is_Cached_Between_Calls_Within_Its_Validity()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, TokenJson);
        var provider = NewProvider(handler);
        var request = Request(EmailProviderKind.GoogleOAuth2);

        await provider.GetAccessTokenAsync(request);
        await provider.GetAccessTokenAsync(request);

        handler.CallCount.Should().Be(1, "un jeton en cache encore valide est réutilisé (pas de second appel réseau)");
    }

    [Fact]
    public async Task No_Secret_Is_Written_To_The_Logs()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, TokenJson);
        var logger = new CapturingLogger<HttpEmailOAuthTokenProvider>();
        var provider = new HttpEmailOAuthTokenProvider(new StubHttpClientFactory(handler), logger);

        await provider.GetAccessTokenAsync(Request(EmailProviderKind.GoogleOAuth2));

        logger.Messages.Should().NotBeEmpty();
        logger.Messages.Should().OnlyContain(
            m => !m.Contains("csecret", StringComparison.Ordinal)
                && !m.Contains("rtoken", StringComparison.Ordinal)
                && !m.Contains("at-123", StringComparison.Ordinal),
            "ni client_secret, ni refresh_token, ni access_token ne doivent apparaître dans un log (CLAUDE.md n°10)");
    }

    [Fact]
    public void The_Token_Request_ToString_Does_Not_Leak_Secrets()
    {
        // Un record synthétise un ToString() imprimant TOUS les membres : les secrets doivent être redactés
        // (CLAUDE.md n°10/18) pour ne jamais fuir par un log/interpolation accidentel.
        var request = Request(EmailProviderKind.GoogleOAuth2);

        request.ToString().Should().NotContain("csecret").And.NotContain("rtoken");
    }

    private static EmailOAuthTokenRequest Request(EmailProviderKind kind, string? tenantId = null) => new()
    {
        Kind = kind,
        ClientId = "cid",
        ClientSecret = "csecret",
        RefreshToken = "rtoken",
        TenantId = tenantId,
    };

    private static HttpEmailOAuthTokenProvider NewProvider(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), new CapturingLogger<HttpEmailOAuthTokenProvider>());

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _responseBody;

        public RecordingHandler(HttpStatusCode status, string responseBody)
        {
            _status = status;
            _responseBody = responseBody;
        }

        public Uri? LastUri { get; private set; }

        public string? LastBody { get; private set; }

        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastUri = request.RequestUri;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
