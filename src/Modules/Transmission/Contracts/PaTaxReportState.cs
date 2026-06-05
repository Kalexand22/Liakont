namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// État d'un tax report côté PA (F05 §3 : <c>new → sent → acknowledged → registered</c>).
/// </summary>
public enum PaTaxReportState
{
    /// <summary>Créé, non encore soumis.</summary>
    New = 0,

    /// <summary>Soumis à l'administration.</summary>
    Sent = 1,

    /// <summary>Accusé de réception reçu.</summary>
    Acknowledged = 2,

    /// <summary>Enregistré par l'administration.</summary>
    Registered = 3,
}
