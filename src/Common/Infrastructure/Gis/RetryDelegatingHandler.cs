namespace Stratum.Common.Infrastructure.Gis;

using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.Gis;

/// <summary>
/// DelegatingHandler that retries transient HTTP failures with exponential backoff.
/// Replaces Polly (forbidden in Phase 1) for OGC service resilience.
/// </summary>
internal sealed partial class RetryDelegatingHandler : DelegatingHandler
{
    private static readonly HashSet<HttpStatusCode> TransientStatusCodes =
    [
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
    ];

    private readonly IOptions<GisOptions> _options;
    private readonly ILogger<RetryDelegatingHandler> _logger;

    public RetryDelegatingHandler(
        IOptions<GisOptions> options,
        ILogger<RetryDelegatingHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var maxRetries = _options.Value.MaxRetries;
        HttpResponseMessage? response = null;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                response = await base.SendAsync(request, cancellationToken);

                if (!IsTransient(response.StatusCode))
                {
                    return response;
                }

                LogTransientStatusCode(_logger, request.RequestUri, (int)response.StatusCode, attempt + 1, maxRetries + 1);
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                LogNetworkError(_logger, ex, request.RequestUri, attempt + 1, maxRetries + 1);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < maxRetries)
            {
                LogTimeout(_logger, ex, request.RequestUri, attempt + 1, maxRetries + 1);
            }

            if (attempt < maxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 500);
                await Task.Delay(delay, cancellationToken);
            }
        }

        return response!;
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        TransientStatusCodes.Contains(statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GIS HTTP request to {Uri} returned {StatusCode}, attempt {Attempt}/{MaxAttempts}")]
    private static partial void LogTransientStatusCode(ILogger logger, Uri? uri, int statusCode, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GIS HTTP request to {Uri} failed with network error, attempt {Attempt}/{MaxAttempts}")]
    private static partial void LogNetworkError(ILogger logger, Exception ex, Uri? uri, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GIS HTTP request to {Uri} timed out, attempt {Attempt}/{MaxAttempts}")]
    private static partial void LogTimeout(ILogger logger, Exception ex, Uri? uri, int attempt, int maxAttempts);
}
