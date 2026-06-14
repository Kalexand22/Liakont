namespace Liakont.Host.Clients;

/// <summary>
/// Résultat de la création du tenant (étape « Créer le client » de l'assistant).
/// </summary>
internal sealed record ClientCreationResult(
    ClientActionStatus Status,
    string? Message = null,
    bool AlreadyProvisioned = false);
