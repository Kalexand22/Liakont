namespace Stratum.Common.UI.Services.Filters;

/// <summary>
/// Result of a single call to an <see cref="IGridFilterAIProvider"/>.
/// </summary>
/// <param name="Success">Whether the LLM returned a parseable response.</param>
/// <param name="ResponseJson">
/// Raw JSON payload returned by the LLM (the inner <c>content</c> string, not the
/// enveloping OpenAI-compatible response). Consumers parse and validate it.
/// </param>
/// <param name="Error">
/// Human-readable error when <paramref name="Success"/> is <c>false</c>
/// (network failure, quota, bad response). Null on success.
/// </param>
public sealed record GridFilterAIProviderResponse(
    bool Success,
    string? ResponseJson,
    string? Error);
