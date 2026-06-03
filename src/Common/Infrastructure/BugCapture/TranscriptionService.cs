namespace Stratum.Common.Infrastructure.BugCapture;

using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

public sealed class TranscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly CaptureConfiguration _config;

    public TranscriptionService(HttpClient httpClient, IOptions<CaptureConfiguration> options)
    {
        _httpClient = httpClient;
        _config = options.Value;
    }

    public async Task<string> TranscribeAsync(byte[] audioBytes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.WhisperApiKey) || audioBytes.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            using var content = new MultipartFormDataContent();
            using var audioContent = new ByteArrayContent(audioBytes);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
            content.Add(audioContent, "file", "audio.webm");
            content.Add(new StringContent(_config.WhisperModel), "model");

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://api.openai.com/v1/audio/transcriptions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.WhisperApiKey);
            request.Content = content;

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return string.Empty;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("text", out var textProp))
            {
                return textProp.GetString() ?? string.Empty;
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
