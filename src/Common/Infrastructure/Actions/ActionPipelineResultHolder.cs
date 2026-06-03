namespace Stratum.Common.Infrastructure.Actions;

using Stratum.Common.Abstractions.Actions;

/// <summary>
/// Scoped holder for the <see cref="ActionResult"/> produced by the pipeline.
/// The behavior writes the result; handlers read it via <see cref="IActionPipelineResult"/>.
/// </summary>
internal sealed class ActionPipelineResultHolder : IActionPipelineResult, IActionPipelineResultWriter
{
    public ActionResult? Result { get; set; }
}
