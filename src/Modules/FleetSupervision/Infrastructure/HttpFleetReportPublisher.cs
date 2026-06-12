namespace Liakont.Modules.FleetSupervision.Infrastructure;

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.FleetSupervision.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Publie le heartbeat d'instance vers l'endpoint central (OPS04) en HTTP JSON, avec la clé d'ingestion en
/// en-tête <c>X-Fleet-Key</c>. NON BLOQUANT : un échec de transport (central injoignable, 4xx/5xx) est
/// journalisé et avalé — le central détectera l'instance muette à l'absence prolongée de heartbeats. Un
/// échec d'envoi de télémétrie ne doit JAMAIS dégrader le service de l'instance.
/// </summary>
internal sealed partial class HttpFleetReportPublisher : IFleetReportPublisher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<FleetSupervisionOptions> _options;
    private readonly ILogger<HttpFleetReportPublisher> _logger;

    public HttpFleetReportPublisher(
        IHttpClientFactory httpClientFactory,
        IOptions<FleetSupervisionOptions> options,
        ILogger<HttpFleetReportPublisher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task PublishAsync(InstanceHeartbeatReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        FleetReportingOptions reporting = _options.Value.Reporting;
        if (string.IsNullOrWhiteSpace(reporting.CentralUrl))
        {
            LogNoCentralUrl(_logger);
            return;
        }

        if (!Uri.TryCreate(CombineUrl(reporting.CentralUrl, FleetApiHeaders.HeartbeatPath), UriKind.Absolute, out Uri? endpoint))
        {
            LogInvalidCentralUrl(_logger, reporting.CentralUrl);
            return;
        }

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(FleetHttpClients.Reporting);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(report, options: FleetTransportJson.Options),
            };
            request.Headers.TryAddWithoutValidation(FleetApiHeaders.Key, reporting.FleetKey);

            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                LogPublished(_logger, reporting.InstanceId, (int)response.StatusCode);
            }
            else
            {
                LogRejected(_logger, reporting.InstanceId, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Non bloquant : on journalise sans relancer (le central verra l'instance muette).
            LogPublishFailed(_logger, reporting.InstanceId, ex.Message);
        }
    }

    private static string CombineUrl(string baseUrl, string path) =>
        $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Méta-supervision : aucune URL centrale configurée, heartbeat non envoyé.")]
    private static partial void LogNoCentralUrl(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Méta-supervision : URL centrale invalide ({CentralUrl}), heartbeat non envoyé.")]
    private static partial void LogInvalidCentralUrl(ILogger logger, string centralUrl);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Heartbeat d'instance {InstanceId} envoyé (HTTP {StatusCode}).")]
    private static partial void LogPublished(ILogger logger, string instanceId, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Heartbeat d'instance {InstanceId} rejeté par le central (HTTP {StatusCode}).")]
    private static partial void LogRejected(ILogger logger, string instanceId, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Heartbeat d'instance {InstanceId} non envoyé (central injoignable) : {Error}.")]
    private static partial void LogPublishFailed(ILogger logger, string instanceId, string error);
}
