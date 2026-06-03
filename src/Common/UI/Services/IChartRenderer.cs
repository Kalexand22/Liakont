namespace Stratum.Common.UI.Services;

using Stratum.Common.UI.Models;

/// <summary>
/// Vendor-agnostic chart rendering abstraction.
/// Implementations translate Stratum chart configuration into library-specific calls
/// via JS interop.
/// </summary>
public interface IChartRenderer
{
    /// <summary>Initialize the chart in the given container element.</summary>
    /// <param name="jsModule">The JS module reference for interop calls.</param>
    /// <param name="containerId">DOM id of the container element.</param>
    /// <param name="config">Serializable chart configuration.</param>
    Task InitializeAsync(
        Microsoft.JSInterop.IJSObjectReference jsModule,
        string containerId,
        ChartConfig config);

    /// <summary>Update the chart with new data or configuration.</summary>
    Task UpdateAsync(
        Microsoft.JSInterop.IJSObjectReference jsModule,
        string containerId,
        ChartConfig config);

    /// <summary>Dispose the chart instance and free resources.</summary>
    Task DisposeAsync(
        Microsoft.JSInterop.IJSObjectReference jsModule,
        string containerId);
}
