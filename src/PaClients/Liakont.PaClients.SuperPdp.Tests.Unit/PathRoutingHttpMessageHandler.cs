namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Net;
using System.Net.Http.Headers;
using System.Text;

/// <summary>
/// Handler HTTP de test routé par SUFFIXE de chemin absolu, pour les tests de bout en bout de la fabrique
/// (où le client tente d'abord un échange de jeton OAuth sur <c>/oauth2/token</c> PUIS l'émission sur
/// <c>/v1.beta/invoices</c> — deux POST que <see cref="RoutedHttpMessageHandler"/> ne distingue pas). Une
/// route inconnue répond 404. Enregistre méthode + chemin + autorisation de chaque requête. Aucune PA réelle.
/// </summary>
internal sealed class PathRoutingHttpMessageHandler : HttpMessageHandler
{
    private readonly List<Route> _routes = [];

    /// <summary>Méthode + chemin + en-tête d'autorisation de chaque requête reçue.</summary>
    public List<(HttpMethod Method, string Path, AuthenticationHeaderValue? Authorization)> Requests { get; } = [];

    /// <summary>Enregistre une réponse pour un suffixe de chemin (ex. <c>/oauth2/token</c>).</summary>
    public PathRoutingHttpMessageHandler On(string pathSuffix, HttpStatusCode status, string body)
    {
        _routes.Add(new Route(pathSuffix, status, body));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        Requests.Add((request.Method, path, request.Headers.Authorization));

        var match = _routes.FirstOrDefault(r => path.EndsWith(r.PathSuffix, StringComparison.Ordinal));
        var response = new HttpResponseMessage(match?.Status ?? HttpStatusCode.NotFound)
        {
            Content = new StringContent(match?.Body ?? string.Empty, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }

    private sealed record Route(string PathSuffix, HttpStatusCode Status, string Body);
}
