namespace Liakont.Host.Clients;

/// <summary>
/// Résultat de la création du tenant (étape « Créer le client » de l'assistant).
/// <see cref="AdminTemporaryPassword"/> : mot de passe temporaire de l'admin initial du realm —
/// remis UNE fois, jamais persisté ni journalisé.
/// </summary>
internal sealed record ClientCreationResult(
    ClientActionStatus Status,
    string? Message = null,
    bool AlreadyProvisioned = false,
    string? AdminTemporaryPassword = null);
