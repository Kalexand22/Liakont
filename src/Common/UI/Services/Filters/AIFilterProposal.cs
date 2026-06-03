namespace Stratum.Common.UI.Services.Filters;

using Stratum.Common.Abstractions.Grid;

/// <summary>
/// Outcome of a natural-language filter request (GFI11).
/// Result of parsing + DF-07 validation of the LLM response.
/// </summary>
/// <param name="Status">High-level outcome. See <see cref="AIFilterProposalStatus"/>.</param>
/// <param name="Criteria">
/// Fully validated filter criteria ready to push into <see cref="GridFilterState"/>.
/// Empty when the proposal was rejected or the LLM returned nothing usable.
/// </param>
/// <param name="Warnings">
/// Non-fatal notices: rejected fields, rejected operators, rejected values, or
/// "nearest match" suggestions. Shown to the user in the confirmation panel.
/// </param>
/// <param name="ErrorMessage">
/// User-facing explanation when <paramref name="Status"/> is <see cref="AIFilterProposalStatus.Unavailable"/>
/// or <see cref="AIFilterProposalStatus.Failed"/>.
/// </param>
public sealed record AIFilterProposal(
    AIFilterProposalStatus Status,
    IReadOnlyList<FilterCriterion> Criteria,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage)
{
    /// <summary>Empty proposal used when the AI service is not configured.</summary>
    public static AIFilterProposal Unavailable(string message) =>
        new(AIFilterProposalStatus.Unavailable, Array.Empty<FilterCriterion>(), Array.Empty<string>(), message);

    /// <summary>Proposal returned when the LLM call failed (network, quota, parse error).</summary>
    public static AIFilterProposal Failed(string message) =>
        new(AIFilterProposalStatus.Failed, Array.Empty<FilterCriterion>(), Array.Empty<string>(), message);
}
