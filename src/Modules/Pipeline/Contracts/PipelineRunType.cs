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

    /// <summary>Agrégation jour×taux de l'e-reporting de paiement depuis les snapshots de ventilation (PIP03a).</summary>
    Aggregate = 3,

    /// <summary>Rectification d'e-reporting (flux RE annule-et-remplace) d'une période déjà déclarée (PIP04).</summary>
    Rectify = 4,

    /// <summary>E-reporting B2C de la marge (flux 10.3, enchères) : agrégation N→1 jour×devise×taux + transmission PA (B4).</summary>
    B2cMarginAggregate = 5,
}
