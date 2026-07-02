namespace Liakont.Host.InstanceEmail;

/// <summary>
/// Résultat de l'envoi d'un email de test (ADR-0039) : un indicateur de succès + un message opérateur en
/// français (CLAUDE.md n°12). Le service ne lève jamais vers l'UI — l'échec est un résultat, jamais une
/// exception non gérée ; aucun secret n'apparaît dans le message.
/// </summary>
public sealed record EmailTestResult
{
    public required bool Success { get; init; }

    public required string Message { get; init; }

    public static EmailTestResult Succeeded(string message) => new() { Success = true, Message = message };

    public static EmailTestResult Failed(string message) => new() { Success = false, Message = message };
}
