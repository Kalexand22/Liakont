namespace Stratum.Common.Abstractions.Actions;

/// <summary>
/// Scoped service that holds the <see cref="ActionResult"/> produced by the
/// <see cref="IActionPipeline"/> during the current request. Handlers can
/// inspect warnings or modified values from the pipeline via this service.
/// </summary>
public interface IActionPipelineResult
{
    /// <summary>
    /// The result of the last pipeline execution in this scope,
    /// or <c>null</c> if no pipeline was executed.
    /// </summary>
    ActionResult? Result { get; }
}

/// <summary>
/// Write-side interface for storing the pipeline result. Used by the
/// <c>ActionPipelineBehavior</c> to store the result without depending
/// on the concrete <c>ActionPipelineResultHolder</c> class.
/// </summary>
public interface IActionPipelineResultWriter
{
    ActionResult? Result { get; set; }
}
