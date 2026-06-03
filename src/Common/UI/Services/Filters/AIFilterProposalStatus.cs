namespace Stratum.Common.UI.Services.Filters;

/// <summary>Outcome classification for <see cref="AIFilterProposal"/>.</summary>
public enum AIFilterProposalStatus
{
    /// <summary>The LLM returned at least one valid criterion.</summary>
    Success,

    /// <summary>
    /// The LLM responded but produced no usable criteria after DF-07 validation.
    /// Warnings explain what was rejected or ambiguous.
    /// </summary>
    Empty,

    /// <summary>Provider is not configured (no API key) — feature disabled.</summary>
    Unavailable,

    /// <summary>The call failed (network, parse, quota). Error message set.</summary>
    Failed,
}
