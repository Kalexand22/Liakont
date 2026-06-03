namespace Stratum.Common.UI.Models;

/// <summary>
/// Supported export formats for StratumDataGrid.
/// Flags enum: multiple formats can be offered simultaneously.
/// </summary>
[Flags]
public enum ExportFormat
{
    Csv = 1,
    Excel = 2,
    Pdf = 4,
}
