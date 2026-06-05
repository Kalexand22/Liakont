namespace Liakont.Agent.Core.Extraction;

/// <summary>
/// Résultat d'un contrôle d'accès à la source (F01-F02 §4.1 — connexion, droits, schéma attendu).
/// Ne lit aucune donnée métier. Sert au diagnostic <c>test-odbc</c> du CLI (AGT05).
/// </summary>
public sealed class HealthCheckResult
{
    private HealthCheckResult(bool isHealthy, string message)
    {
        IsHealthy = isHealthy;
        Message = message;
    }

    /// <summary>La source est accessible dans l'état attendu.</summary>
    public bool IsHealthy { get; }

    /// <summary>Message opérateur en français (détail du succès ou de l'échec, action corrective).</summary>
    public string Message { get; }

    /// <summary>Crée un résultat sain.</summary>
    /// <param name="message">Message de diagnostic (ex. tables détectées).</param>
    public static HealthCheckResult Healthy(string message) => new HealthCheckResult(true, message);

    /// <summary>Crée un résultat en échec.</summary>
    /// <param name="message">Message opérateur expliquant l'échec et l'action corrective.</param>
    public static HealthCheckResult Unhealthy(string message) => new HealthCheckResult(false, message);
}
