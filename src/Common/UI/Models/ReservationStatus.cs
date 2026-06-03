namespace Stratum.Common.UI.Models;

/// <summary>
/// FSM statuses recognised by <see cref="Components.ReservationStatusChip"/>
/// and by <see cref="Components.StatusBadge"/> when <c>Intent="Fsm"</c>.
/// </summary>
/// <remarks>
/// This enum is a UI-side mirror of the Reservation domain FSM defined in
/// <c>Stratum.Modules.Reservation.Domain.Entities.ReservationStatus</c>. It is
/// duplicated here so that <c>Stratum.Common.UI</c> stays free of cross-module
/// dependencies (the UI layer must never reference a feature module). Adding,
/// removing, or renaming a value here MUST be mirrored in the domain enum and
/// in the FSM token set in <c>tokens.css</c> (<c>--fsm-*-bg</c>/<c>--fsm-*-fg</c>).
/// </remarks>
public enum ReservationStatus
{
    /// <summary>Initial draft, not yet submitted by the requester.</summary>
    Draft,

    /// <summary>Submitted by the requester, awaiting triage.</summary>
    Submitted,

    /// <summary>Under qualification (completeness checks).</summary>
    Qualifying,

    /// <summary>Under instruction by an agent.</summary>
    UnderReview,

    /// <summary>Validated — approved but resources not yet committed.</summary>
    Validated,

    /// <summary>Reserved — resources held, work order not yet started.</summary>
    Reserved,

    /// <summary>Preparation phase (logistics, agents being dispatched).</summary>
    InPreparation,

    /// <summary>Currently in execution.</summary>
    InExecution,

    /// <summary>Terminal — successfully completed.</summary>
    Completed,

    /// <summary>Terminal — refused by the agent at instruction time.</summary>
    Refused,

    /// <summary>Terminal — cancelled (by requester or agent) before completion.</summary>
    Cancelled,
}
