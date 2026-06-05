namespace Liakont.Agent.Cli;

/// <summary>
/// Codes de retour du CLI de diagnostic (F12 §2.1, AGT05). Un intégrateur (ou un script
/// d'installation) s'appuie dessus pour enchaîner les étapes de mise en service.
/// </summary>
internal static class CliExitCode
{
    /// <summary>Tout est conforme.</summary>
    public const int Ok = 0;

    /// <summary>Un problème a été détecté (configuration invalide, source injoignable, clé refusée…).</summary>
    public const int ProblemDetected = 1;

    /// <summary>Erreur d'exécution du CLI lui-même (argument manquant, exception inattendue).</summary>
    public const int ExecutionError = 2;
}
