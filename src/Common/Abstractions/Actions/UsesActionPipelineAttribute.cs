namespace Stratum.Common.Abstractions.Actions;

/// <summary>
/// Marker attribute placed on MediatR commands to indicate that the
/// <see cref="IActionPipeline"/> should be executed before the handler.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UsesActionPipelineAttribute : Attribute;
