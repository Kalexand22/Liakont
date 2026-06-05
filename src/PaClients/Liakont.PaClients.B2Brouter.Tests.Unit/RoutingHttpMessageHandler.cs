namespace Liakont.PaClients.B2Brouter.Tests.Unit;

using System.Net;
using System.Text;

/// <summary>
/// Handler HTTP de test à ROUTAGE par (méthode + chemin absolu), nécessaire aux lectures PAB03
/// (List/Get tax reports, compte, réglage) et au flux idempotent GET → POST/PATCH d'
/// <c>EnsureTaxReportSettingAsync</c>. Enregistre TOUTES les requêtes (méthode, URI, corps) pour les
/// assertions de payload « fil » et de NON-appel (idempotence). Une route non enregistrée répond 404
/// (cas « réglage absent »). Aucune PA réelle n'est appelée. Complète <see cref="StubHttpMessageHandler"/>
/// (réponse unique de PAB01) sans le modifier.
/// </summary>
internal sealed class RoutingHttpMessageHandler : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = [];
    private readonly List<Route> _routes = [];

    /// <summary>Toutes les requêtes reçues, dans l'ordre.</summary>
    public IReadOnlyList<RecordedRequest> Requests => _requests;

    /// <summary>Nombre total d'appels reçus (assertions d'idempotence / non-appel).</summary>
    public int CallCount => _requests.Count;

    /// <summary>Enregistre une réponse pour une méthode + un chemin absolu. Les routes d'une même paire
    /// sont consommées dans l'ordre d'ajout (séquence), la dernière persistant ensuite.</summary>
    /// <param name="method">Méthode HTTP attendue.</param>
    /// <param name="absolutePath">Chemin absolu attendu (ex. <c>/accounts/ACC-42/tax_reports.json</c>).</param>
    /// <param name="status">Code HTTP à renvoyer.</param>
    /// <param name="body">Corps JSON à renvoyer (vide par défaut).</param>
    public RoutingHttpMessageHandler On(HttpMethod method, string absolutePath, HttpStatusCode status, string body = "")
    {
        _routes.Add(new Route(method, absolutePath, status, body));
        return this;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        _requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body));

        var path = request.RequestUri!.AbsolutePath;
        var match = _routes.FirstOrDefault(r => !r.Consumed && r.Matches(request.Method, path))
            ?? _routes.LastOrDefault(r => r.Matches(request.Method, path));

        if (match is null)
        {
            // Route inconnue → 404 (ex. réglage de tax report pas encore créé).
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json"),
            };
        }

        match.Consumed = true;
        return new HttpResponseMessage(match.Status)
        {
            Content = new StringContent(match.Body, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>Une requête capturée (méthode, URI, corps).</summary>
    /// <param name="Method">Méthode HTTP.</param>
    /// <param name="Uri">URI complète.</param>
    /// <param name="Body">Corps de la requête, ou <c>null</c> si aucun.</param>
    internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body);

    private sealed class Route(HttpMethod method, string path, HttpStatusCode status, string body)
    {
        public HttpStatusCode Status { get; } = status;

        public string Body { get; } = body;

        public bool Consumed { get; set; }

        public bool Matches(HttpMethod requestMethod, string requestPath) =>
            method == requestMethod && string.Equals(path, requestPath, StringComparison.Ordinal);
    }
}
