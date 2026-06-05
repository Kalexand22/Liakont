namespace Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Nature d'une exécution du pipeline (PIP01) journalisée dans <c>pipeline.run_logs</c> : contrôle
/// pré-envoi (CHECK), envoi à la Plateforme Agréée (SEND), ou synchronisation des justificatifs (SYNC).
/// PIP01a ne fait que définir le type ; les exécutions sont écrites par PIP01b+ (CHECK/SEND/SYNC).
/// </summary>
public enum PipelineRunType
{
    /// <summary>Contrôle pré-envoi : relecture du staging → mapping TVA → validation → ReadyToSend/Blocked (PIP01b).</summary>
    Check = 0,

    /// <summary>Envoi des documents prêts à la Plateforme Agréée (PIP01c).</summary>
    Send = 1,

    /// <summary>Synchronisation des justificatifs (tax reports, facture PA) vers l'archive (PIP01d).</summary>
    Sync = 2,
}
