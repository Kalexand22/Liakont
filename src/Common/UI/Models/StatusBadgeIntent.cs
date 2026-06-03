namespace Stratum.Common.UI.Models;

/// <summary>
/// Selects which colour vocabulary <see cref="Components.StatusBadge"/> renders.
/// </summary>
public enum StatusBadgeIntent
{
    /// <summary>
    /// Default. The badge is coloured by its <c>Severity</c> parameter
    /// (Info / Success / Warning / Error / Neutral) using the legacy
    /// <c>--color-severity-*</c> tokens.
    /// </summary>
    Severity,

    /// <summary>
    /// FSM intent — the badge maps a <see cref="ReservationStatus"/> value
    /// to the Civic Blueprint FSM container tokens (<c>--fsm-*-bg</c>/
    /// <c>--fsm-*-fg</c>). The <c>FsmStatus</c> parameter must be set when
    /// this intent is selected.
    /// </summary>
    Fsm,
}
