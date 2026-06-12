namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System.Net;
using System.Net.Http.Headers;
using System.Text;

/// <summary>
/// Handler HTTP de test ROUTÉ par méthode + chemin, pour les scénarios de retry / idempotence / auth de
/// PAS02 (F14 §4.1-§4.2 ; §3.1 refresh OAuth). Quatre files de réponses scriptées :
/// <list type="bullet">
///   <item><c>POST /v1.beta/invoices/convert</c> → file <see cref="OnConvert"/> (conversion en16931 → CII).</item>
///   <item><c>POST /v1.beta/invoices</c> → file <see cref="OnPost"/> (émission du XML).</item>
///   <item><c>GET /v1.beta/invoices</c> → file <see cref="OnListInvoices"/> (relecture d'idempotence).</item>
///   <item><c>GET /v1.beta/invoices/{id}</c> → file <see cref="OnGetInvoice"/> (relecture d'état).</item>
/// </list>
/// Quand une file est épuisée, la DERNIÈRE réponse est rejouée. Toutes les requêtes (méthode, URI, corps,
/// jeton bearer) sont enregistrées pour assertion — notamment pour vérifier que le 401 déclenche bien un
/// refresh de jeton et une seconde tentative. Aucune PA réelle n'est appelée (mock HTTP exigé par PAS02).
/// </summary>
internal sealed class RoutedHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<ResponseStep> _convert = new();
    private readonly Queue<ResponseStep> _post = new();
    private readonly Queue<ResponseStep> _list = new();
    private readonly Queue<ResponseStep> _detail = new();
    private ResponseStep? _lastConvert;
    private ResponseStep? _lastPost;
    private ResponseStep? _lastList;
    private ResponseStep? _lastDetail;

    /// <summary>Toutes les requêtes reçues, dans l'ordre (méthode, URI, chemin, corps, autorisation).</summary>
    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Nombre de POST de conversion reçus (en16931 → CII).</summary>
    public int ConvertCount => Requests.Count(r => r.Method == HttpMethod.Post && IsConvertPath(r.Path));

    /// <summary>Nombre de POST d'ÉMISSION reçus (hors conversion) — la mesure anti-doublon (F14 §4.1).</summary>
    public int PostCount => Requests.Count(r => r.Method == HttpMethod.Post && !IsConvertPath(r.Path));

    /// <summary>Nombre de GET de liste reçus (relectures d'idempotence).</summary>
    public int ListCount => Requests.Count(r => r.Method == HttpMethod.Get && IsListPath(r.Path));

    /// <summary>Nombre de GET de détail reçus (relectures d'état).</summary>
    public int DetailCount => Requests.Count(r => r.Method == HttpMethod.Get && !IsListPath(r.Path));

    /// <summary>Programme une réponse à la prochaine CONVERSION (200 = le XML CII).</summary>
    public RoutedHttpMessageHandler OnConvert(HttpStatusCode status, string body)
    {
        _convert.Enqueue(ResponseStep.Respond(status, body));
        return this;
    }

    /// <summary>Programme une exception (réseau/timeout) à la prochaine conversion.</summary>
    public RoutedHttpMessageHandler OnConvertThrows(Exception toThrow)
    {
        _convert.Enqueue(ResponseStep.Throw(toThrow));
        return this;
    }

    /// <summary>Programme une réponse au prochain POST d'émission.</summary>
    public RoutedHttpMessageHandler OnPost(HttpStatusCode status, string body)
    {
        _post.Enqueue(ResponseStep.Respond(status, body));
        return this;
    }

    /// <summary>Programme une exception (réseau/timeout) au prochain POST d'émission.</summary>
    public RoutedHttpMessageHandler OnPostThrows(Exception toThrow)
    {
        _post.Enqueue(ResponseStep.Throw(toThrow));
        return this;
    }

    /// <summary>Programme une réponse à la prochaine relecture de liste (idempotence).</summary>
    public RoutedHttpMessageHandler OnListInvoices(HttpStatusCode status, string body)
    {
        _list.Enqueue(ResponseStep.Respond(status, body));
        return this;
    }

    /// <summary>Programme une exception (réseau/timeout) à la prochaine relecture de liste.</summary>
    public RoutedHttpMessageHandler OnListInvoicesThrows(Exception toThrow)
    {
        _list.Enqueue(ResponseStep.Throw(toThrow));
        return this;
    }

    /// <summary>Programme une réponse à la prochaine relecture d'état (GET /v1.beta/invoices/{id}).</summary>
    public RoutedHttpMessageHandler OnGetInvoice(HttpStatusCode status, string body)
    {
        _detail.Enqueue(ResponseStep.Respond(status, body));
        return this;
    }

    /// <summary>Programme une exception (réseau/timeout) à la prochaine relecture d'état.</summary>
    public RoutedHttpMessageHandler OnGetInvoiceThrows(Exception toThrow)
    {
        _detail.Enqueue(ResponseStep.Throw(toThrow));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        string? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        Requests.Add(new RecordedRequest(request.Method, request.RequestUri, path, body, request.Headers.Authorization));

        var step = Next(request.Method, path);
        if (step.ToThrow is not null)
        {
            throw step.ToThrow;
        }

        return new HttpResponseMessage(step.Status)
        {
            Content = new StringContent(step.Body, Encoding.UTF8, "application/json"),
        };
    }

    private static bool IsListPath(string path) =>
        path.EndsWith("/invoices", StringComparison.Ordinal);

    private static bool IsConvertPath(string path) =>
        path.EndsWith("/invoices/convert", StringComparison.Ordinal);

    private static ResponseStep Dequeue(Queue<ResponseStep> queue, ref ResponseStep? last, string label)
    {
        if (queue.Count > 0)
        {
            last = queue.Dequeue();
        }

        return last ?? throw new InvalidOperationException(
            $"Aucune réponse scriptée pour {label} dans RoutedHttpMessageHandler.");
    }

    private ResponseStep Next(HttpMethod method, string path)
    {
        if (method == HttpMethod.Post)
        {
            return IsConvertPath(path)
                ? Dequeue(_convert, ref _lastConvert, "POST conversion")
                : Dequeue(_post, ref _lastPost, "POST émission");
        }

        return IsListPath(path)
            ? Dequeue(_list, ref _lastList, "GET liste")
            : Dequeue(_detail, ref _lastDetail, "GET détail");
    }

    /// <summary>Une requête enregistrée (méthode, URI, chemin, corps, autorisation).</summary>
    /// <param name="Method">Méthode HTTP.</param>
    /// <param name="Uri">URI complète (peut être <c>null</c>).</param>
    /// <param name="Path">Chemin absolu (pour le routage des assertions).</param>
    /// <param name="Body">Corps de la requête (POST), ou <c>null</c>.</param>
    /// <param name="Authorization">En-tête <c>Authorization</c> reçu (jeton bearer), ou <c>null</c>.</param>
    internal sealed record RecordedRequest(
        HttpMethod Method, Uri? Uri, string Path, string? Body, AuthenticationHeaderValue? Authorization);

    private sealed record ResponseStep(HttpStatusCode Status, string Body, Exception? ToThrow)
    {
        public static ResponseStep Respond(HttpStatusCode status, string body) => new(status, body, null);

        public static ResponseStep Throw(Exception toThrow) => new(HttpStatusCode.OK, string.Empty, toThrow);
    }
}
