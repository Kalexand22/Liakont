namespace Stratum.Common.UI.Models;

/// <summary>
/// Arguments passed to <c>OnExport</c> when the user triggers a data export from StratumDataGrid.
/// </summary>
/// <param name="Format">The export format requested by the user.</param>
/// <param name="FileName">Suggested file name (without extension).</param>
public sealed record ExportArgs(
    ExportFormat Format,
    string FileName);
