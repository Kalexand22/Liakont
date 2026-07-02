namespace Liakont.Host.Tests.Unit.InstanceEmail;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.InstanceEmail;

/// <summary>Faux fournisseur de jeton OAuth : rend un jeton fixe et mémorise la dernière requête reçue.</summary>
internal sealed class FakeEmailOAuthTokenProvider : IEmailOAuthTokenProvider
{
    public EmailOAuthTokenRequest? LastRequest { get; private set; }

    public string Token { get; set; } = "fake-access-token";

    public Task<string> GetAccessTokenAsync(EmailOAuthTokenRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(Token);
    }
}
