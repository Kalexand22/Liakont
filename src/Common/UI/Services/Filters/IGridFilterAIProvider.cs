namespace Stratum.Common.UI.Services.Filters;

/// <summary>
/// Abstraction over the LLM backend used by <see cref="GridFilterAIService"/>.
/// Implementations return the raw JSON response; parsing and validation happen
/// in the service layer so the provider stays a thin transport concern.
/// </summary>
public interface IGridFilterAIProvider
{
    /// <summary>
    /// <c>true</c> when the provider is configured (e.g. API key present) and can
    /// be called. When <c>false</c>, <see cref="GridFilterAIService"/> skips the network
    /// call and the UI exposes a disabled state with fallback messaging.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Runs a single completion request and returns the raw JSON payload.</summary>
    /// <param name="prompt">
    /// Full prompt including column registry context, operator map, and user input.
    /// Built by <see cref="GridFilterAIService"/> — providers must not mutate it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<GridFilterAIProviderResponse> CompleteAsync(string prompt, CancellationToken cancellationToken = default);
}
