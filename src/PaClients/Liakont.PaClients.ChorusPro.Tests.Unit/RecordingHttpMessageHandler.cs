namespace Liakont.PaClients.ChorusPro.Tests.Unit;

using System.Net;
using System.Net.Http.Headers;
using System.Text;

/// <summary>
/// Handler HTTP de test : ENREGISTRE chaque requête reçue (URI, corps, en-tête <c>Authorization</c> bearer
/// et en-tête <c>cpro-account</c>) et rejoue une FILE de réponses scriptées (code + corps), ou lève une
/// exception fixée (réseau / timeout). Quand la file est épuisée, la dernière réponse est rejouée. Aucune
/// PA réelle n'est appelée — c'est le mock HTTP exigé pour exercer la double authentification + le retry
/// <c>401</c> de CP03 (F18 §2).
/// </summary>
internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<ResponseStep> _steps = new();
    private ResponseStep? _last;

    /// <summary>Toutes les requêtes reçues, dans l'ordre.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Nombre de requêtes reçues.</summary>
    public int CallCount => Requests.Count;

    /// <summary>Programme la prochaine réponse (code HTTP + corps).</summary>
    public RecordingHttpMessageHandler Respond(HttpStatusCode status, string body = "{}")
    {
        _steps.Enqueue(ResponseStep.Of(status, body, null));
        return this;
    }

    /// <summary>Programme une exception (réseau / timeout) à la prochaine requête.</summary>
    public RecordingHttpMessageHandler Throws(Exception toThrow)
    {
        _steps.Enqueue(ResponseStep.Of(HttpStatusCode.OK, string.Empty, toThrow));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        var technicalAccount = request.Headers.TryGetValues("cpro-account", out var values)
            ? string.Join(",", values)
            : null;

        Requests.Add(new RecordedRequest(request.RequestUri, body, request.Headers.Authorization, technicalAccount));

        if (_steps.Count > 0)
        {
            _last = _steps.Dequeue();
        }

        var step = _last ?? throw new InvalidOperationException(
            "Aucune réponse scriptée dans RecordingHttpMessageHandler.");

        if (step.ToThrow is not null)
        {
            throw step.ToThrow;
        }

        return new HttpResponseMessage(step.Status)
        {
            Content = new StringContent(step.Body, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>Une requête enregistrée (URI, corps, bearer, en-tête compte technique).</summary>
    /// <param name="Uri">URI complète (peut être <c>null</c>).</param>
    /// <param name="Body">Corps de la requête, ou <c>null</c>.</param>
    /// <param name="Authorization">En-tête <c>Authorization</c> reçu (jeton bearer), ou <c>null</c>.</param>
    /// <param name="TechnicalAccount">Valeur de l'en-tête <c>cpro-account</c> reçue, ou <c>null</c>.</param>
    internal sealed record RecordedRequest(
        Uri? Uri, string? Body, AuthenticationHeaderValue? Authorization, string? TechnicalAccount);

    private sealed record ResponseStep(HttpStatusCode Status, string Body, Exception? ToThrow)
    {
        public static ResponseStep Of(HttpStatusCode status, string body, Exception? toThrow) =>
            new(status, body, toThrow);
    }
}
