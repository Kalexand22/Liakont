namespace Stratum.Common.Infrastructure.Actions.Chains;

/// <summary>
/// Describes a single step in an action chain.
/// </summary>
/// <param name="ServiceType">The DI service type to resolve (validator or action step).</param>
/// <param name="Kind">Whether this is a Validate, Execute, or Notify step.</param>
/// <param name="Condition">Optional condition (object to support generic predicates).</param>
internal sealed record ChainStepDescriptor(Type ServiceType, ChainStepKind Kind, object? Condition);
