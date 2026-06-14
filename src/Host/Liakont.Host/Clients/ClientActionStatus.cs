namespace Liakont.Host.Clients;

/// <summary>Issue d'une action de l'écran Clients (refus métier ≠ panne — message opérateur en français).</summary>
internal enum ClientActionStatus
{
    Succeeded,
    AlreadyExists,
    NotFound,
    Conflict,
    ValidationFailed,
    Failed,
}
