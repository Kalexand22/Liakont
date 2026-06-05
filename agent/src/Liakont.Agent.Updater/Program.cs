namespace Liakont.Agent.Updater;

using System;

/// <summary>
/// Point d'entrée de l'updater détaché (AGT04, ADR-0013). Câble les implémentations réelles (SCM,
/// remplacement de fichiers, sonde de santé, log), exécute le <see cref="UpdaterEngine"/>, écrit le
/// statut pour l'agent, et rend un code de retour : 0 = appliqué, 1 = rollback, 2 = échec.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (!UpdaterArguments.TryParse(args, out UpdaterPlan? plan, out string? logPath, out string? error))
        {
            Console.Error.WriteLine("Updater : " + error);
            return (int)UpdaterOutcome.Failed;
        }

        IUpdaterLog log = new FileUpdaterLog(logPath ?? string.Empty);
        var engine = new UpdaterEngine(
            new ScmServiceControl(),
            new FileBinarySwapper(),
            new LocalHeartbeatHealthProbe(),
            log);

        UpdaterResult result = engine.Run(plan!);
        UpdaterStatusWriter.Write(plan!.StatusPath, plan.TargetVersion, result);
        return (int)result.Outcome;
    }
}
