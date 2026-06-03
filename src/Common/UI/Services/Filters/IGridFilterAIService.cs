namespace Stratum.Common.UI.Services.Filters;

using Stratum.Common.UI.Models;

/// <summary>
/// Service that converts natural-language filter requests into validated
/// <see cref="Stratum.Common.Abstractions.Grid.FilterCriterion"/> proposals (GFI11).
/// The service composes a structured prompt from the grid's column registry and
/// delegates to <see cref="IGridFilterAIProvider"/> for the actual LLM call.
/// All LLM output is validated against DF-07 (allowed fields, operators, values)
/// before being surfaced to the UI.
/// </summary>
public interface IGridFilterAIService
{
    /// <summary>
    /// <c>true</c> when the underlying provider is configured and reachable.
    /// Mirrors <see cref="IGridFilterAIProvider.IsAvailable"/>; the UI uses it
    /// to disable the assist button + show a fallback message.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Converts a free-form user request (typed or dictated) into a validated
    /// proposal of filter criteria scoped to the given columns.
    /// </summary>
    /// <param name="columns">
    /// Columns the user may filter on — typically
    /// <c>IColumnRegistry&lt;TItem&gt;.GetAvailableColumns()</c>. Must not be empty.
    /// </param>
    /// <param name="userInput">Natural-language filter request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AIFilterProposal> GenerateAsync(
        IReadOnlyList<ColumnDefinition> columns,
        string userInput,
        CancellationToken cancellationToken = default);
}
