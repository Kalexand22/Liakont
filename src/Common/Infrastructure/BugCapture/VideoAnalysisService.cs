namespace Stratum.Common.Infrastructure.BugCapture;

using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Sends a screen recording to OpenRouter (Gemini) for AI-powered summarization
/// and key-moment identification. Silent failure — never blocks report submission.
/// </summary>
public sealed partial class VideoAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly CaptureConfiguration _config;
    private readonly ILogger<VideoAnalysisService> _logger;

    public VideoAnalysisService(
        HttpClient httpClient,
        IOptions<CaptureConfiguration> options,
        ILogger<VideoAnalysisService> logger)
    {
        _httpClient = httpClient;
        _config = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Analyzes a screen recording and returns a structured summary with key moments.
    /// </summary>
    /// <param name="videoBytes">WebM video bytes.</param>
    /// <param name="culture">Current UI culture — prompt language adapts accordingly.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Analysis result, or empty analysis if unconfigured or on failure.</returns>
    public async Task<VideoAnalysis> AnalyzeAsync(
        byte[] videoBytes,
        CultureInfo? culture = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.OpenRouterApiKey) || videoBytes.Length == 0)
        {
            return new VideoAnalysis();
        }

        try
        {
            var base64Video = Convert.ToBase64String(videoBytes);
            var dataUri = string.Concat("data:video/webm;base64,", base64Video);
            var prompt = BuildPrompt(culture);

            var payload = new
            {
                model = _config.VideoAnalysisModel,
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "image_url", image_url = new { url = dataUri } },
                            new { type = "text", text = prompt },
                        },
                    },
                },
                response_format = new { type = "json_object" },
            };

            var json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.OpenRouterApiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new VideoAnalysis();
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = ParseResponse(responseJson);

            if (result.Summary.Length > 0)
            {
                LogAnalysisSuccess(_logger, result.Summary.Length, result.KeyMoments.Count);
            }
            else
            {
                LogAnalysisEmpty(_logger, responseJson);
            }

            return result;
        }
        catch (Exception ex)
        {
            LogAnalysisFailed(_logger, ex);
            return new VideoAnalysis();
        }
    }

    private static string BuildPrompt(CultureInfo? culture)
    {
        var isFrench = culture?.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase) is true;

        if (isFrench)
        {
            // Liakont: la narration audio de l'utilisateur est la source PRINCIPALE du rapport
            // (sans cette consigne, le modèle décrit l'écran et ignore la dictée).
            return """
                Tu es un testeur QA qui analyse un enregistrement d'écran d'une application web.
                Si l'enregistrement contient une narration audio de l'utilisateur, c'est la source
                PRINCIPALE du rapport : écoute-la attentivement et base le titre, le résumé et les
                étapes sur ce que la voix décrit. La vidéo sert à illustrer et préciser la narration.
                Génère un rapport de bug structuré en français.

                Règles :
                - "title" : titre court et actionnable du problème (max 10 mots, ex: "Le DatePicker ne se ferme pas après sélection")
                - "summary" : description factuelle du problème observé en 2-3 phrases max. Pas de narration ("l'utilisateur fait..."), va droit au problème.
                - "steps" : étapes pour reproduire le bug, une par ligne, numérotées (ex: "1. Ouvrir la page X\n2. Cliquer sur Y")
                - "key_moments" : moments clés avec timestamps

                Si aucun bug n'est décrit par la voix ni visible à l'écran, décris simplement ce qui se passe.

                Réponds en JSON strict :
                {"title": "...", "summary": "...", "steps": "1. ...\n2. ...", "key_moments": [{"timestamp_seconds": N, "description": "..."}]}
                """;
        }

        // Liakont: the user's audio narration is the PRIMARY source of the report
        // (without this instruction, the model describes the screen and ignores the dictation).
        return """
            You are a QA tester analyzing a screen recording of a web application.
            If the recording contains the user's audio narration, it is the PRIMARY source of
            the report: listen to it carefully and base the title, summary and steps on what
            the voice describes. The video serves to illustrate and refine the narration.
            Generate a structured bug report in English.

            Rules:
            - "title": short actionable title of the issue (max 10 words, e.g. "DatePicker doesn't close after selection")
            - "summary": factual description of the observed problem in 2-3 sentences max. No narration ("the user does..."), go straight to the problem.
            - "steps": steps to reproduce, one per line, numbered (e.g. "1. Open page X\n2. Click Y")
            - "key_moments": key moments with timestamps

            If no bug is described by the voice nor visible on screen, simply describe what happens.

            Respond in strict JSON:
            {"title": "...", "summary": "...", "steps": "1. ...\n2. ...", "key_moments": [{"timestamp_seconds": N, "description": "..."}]}
            """;
    }

    private static VideoAnalysis ParseResponse(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        // OpenAI-compatible format: choices[0].message.content
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            return new VideoAnalysis();
        }

        var content = choices[0].GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            return new VideoAnalysis();
        }

        // Strip markdown code fences (```json ... ``` or ``` ... ```)
        content = StripCodeFences().Replace(content, "$1").Trim();

        // Fallback: extract the first JSON object by braces if fences weren't fully stripped.
        var firstBrace = content.IndexOf('{');
        var lastBrace = content.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            content = content[firstBrace..(lastBrace + 1)];
        }

        using var contentDoc = JsonDocument.Parse(content);
        var contentRoot = contentDoc.RootElement;

        var title = TryGetStringProperty(contentRoot, "title", "titre");
        var summary = TryGetStringProperty(contentRoot, "summary", "résumé", "resume");
        var steps = TryGetStringProperty(contentRoot, "steps", "étapes", "etapes", "steps_to_reproduce");

        var keyMoments = new List<VideoKeyMoment>();

        // Accept "key_moments", "keyMoments", or "moments_cles".
        var momentsProp = TryGetArrayProperty(contentRoot, "key_moments", "keyMoments", "moments_cles");

        if (momentsProp is not null)
        {
            foreach (var moment in momentsProp.Value.EnumerateArray())
            {
                var ts = TryGetDoubleProperty(moment, "timestamp_seconds", "timestamp", "time");

                var desc = TryGetStringProperty(moment, "description", "desc");

                keyMoments.Add(new VideoKeyMoment
                {
                    TimestampSeconds = ts,
                    Description = desc,
                });
            }
        }

        return new VideoAnalysis
        {
            Title = title,
            Summary = summary,
            StepsToReproduce = steps,
            KeyMoments = keyMoments,
        };
    }

    private static string TryGetStringProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static double TryGetDoubleProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetDouble();
            }
        }

        return 0;
    }

    private static JsonElement? TryGetArrayProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Array)
            {
                return prop;
            }
        }

        return null;
    }

    [GeneratedRegex(@"```(?:json)?\s*([\s\S]*?)```", RegexOptions.None)]
    private static partial Regex StripCodeFences();

    [LoggerMessage(Level = LogLevel.Information, Message = "Video analysis complete: {SummaryLength} chars, {MomentCount} key moments")]
    private static partial void LogAnalysisSuccess(ILogger logger, int summaryLength, int momentCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Video analysis returned empty result. Raw response: {Response}")]
    private static partial void LogAnalysisEmpty(ILogger logger, string response);

    [LoggerMessage(Level = LogLevel.Error, Message = "Video analysis failed")]
    private static partial void LogAnalysisFailed(ILogger logger, Exception exception);
}
