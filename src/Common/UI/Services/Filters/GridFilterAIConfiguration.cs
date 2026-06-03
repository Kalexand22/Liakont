namespace Stratum.Common.UI.Services.Filters;

/// <summary>
/// Configuration for the grid filter AI assistant (GFI11).
/// When <see cref="ApiKey"/> is empty, the service reports itself as unavailable
/// and the UI falls back to a disabled button + fallback message.
/// </summary>
public sealed record GridFilterAIConfiguration
{
    /// <summary>OpenRouter (or compatible) API key. Null/empty disables the feature.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Model identifier used for completion requests.</summary>
    public string Model { get; init; } = "anthropic/claude-3.5-haiku";

    /// <summary>OpenAI-compatible chat completion endpoint.</summary>
    public string Endpoint { get; init; } = "https://openrouter.ai/api/v1/chat/completions";

    /// <summary>Request timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 30;
}
