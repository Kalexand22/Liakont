namespace Stratum.Common.UI.Services.Filters;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Default <see cref="IGridFilterAIProvider"/> backed by the OpenRouter
/// OpenAI-compatible chat completion API. When no API key is configured the
/// provider reports itself unavailable and returns without issuing any HTTP call.
/// </summary>
public sealed partial class OpenRouterGridFilterAIProvider : IGridFilterAIProvider
{
    private readonly HttpClient _httpClient;
    private readonly GridFilterAIConfiguration _config;
    private readonly ILogger<OpenRouterGridFilterAIProvider> _logger;

    public OpenRouterGridFilterAIProvider(
        HttpClient httpClient,
        IOptions<GridFilterAIConfiguration> options,
        ILogger<OpenRouterGridFilterAIProvider> logger)
    {
        _httpClient = httpClient;
        _config = options.Value;
        _logger = logger;
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_config.ApiKey);

    public async Task<GridFilterAIProviderResponse> CompleteAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return new GridFilterAIProviderResponse(
                Success: false,
                ResponseJson: null,
                Error: "AI provider not configured.");
        }

        var payload = new
        {
            model = _config.Model,
            messages = new object[]
            {
                new { role = "user", content = prompt },
            },
            response_format = new { type = "json_object" },
            temperature = 0.0,
        };

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, _config.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeoutSeconds = _config.TimeoutSeconds > 0 ? _config.TimeoutSeconds : 30;
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);

            var responseBody = await response.Content
                .ReadAsStringAsync(cts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogHttpFailure((int)response.StatusCode, Truncate(responseBody, 512));

                return new GridFilterAIProviderResponse(
                    Success: false,
                    ResponseJson: null,
                    Error: $"Erreur HTTP {(int)response.StatusCode}");
            }

            return new GridFilterAIProviderResponse(
                Success: true,
                ResponseJson: responseBody,
                Error: null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogHttpTimeout(_config.TimeoutSeconds);
            return new GridFilterAIProviderResponse(false, null, "L'assistant IA n'a pas répondu à temps.");
        }
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    [LoggerMessage(
        EventId = 6110,
        Level = LogLevel.Warning,
        Message = "Grid filter AI HTTP call failed: {Status} — {Body}")]
    private partial void LogHttpFailure(int status, string body);

    [LoggerMessage(
        EventId = 6111,
        Level = LogLevel.Warning,
        Message = "Grid filter AI HTTP call timed out after {Seconds}s.")]
    private partial void LogHttpTimeout(int seconds);
}
