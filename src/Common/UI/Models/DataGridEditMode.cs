namespace Stratum.Common.UI.Models;

/// <summary>
/// Controls how inline editing is triggered on StratumDataGrid.
/// </summary>
public enum DataGridEditMode
{
    /// <summary>Editing requires explicit call (e.g. edit button). Default.</summary>
    ButtonOnly,

    /// <summary>Single-click on a row enters edit mode.</summary>
    Click,

    /// <summary>Double-click on a row enters edit mode.</summary>
    DoubleClick,
}
