namespace Stratum.Common.Infrastructure.Actions.Chains;

/// <summary>
/// Categorizes the behavior of a chain step.
/// </summary>
internal enum ChainStepKind
{
    /// <summary>Validation step: always runs, stop-on-error.</summary>
    Validate,

    /// <summary>Execution step: may have a condition, stop-on-error.</summary>
    Execute,

    /// <summary>Notification step: may have a condition, never blocks the chain.</summary>
    Notify,
}
