namespace Stratum.Common.UI.Models;

/// <summary>Controls how <c>PermissionGate</c> renders when the user lacks the required permission.</summary>
public enum GateMode
{
    /// <summary>Hides the child content entirely. Renders <c>Fallback</c> if provided.</summary>
    Hide,

    /// <summary>Renders the child content in a disabled wrapper with an optional tooltip.</summary>
    Disable,
}
