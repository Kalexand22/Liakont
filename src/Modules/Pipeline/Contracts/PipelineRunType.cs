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

    /// <summary>E-reporting B2C au régime du prix total taxable (flux 10.3, enchères, TLB1) : agrégation N→1 jour×devise×taux + transmission PA (BUG-8).</summary>
    B2cTaxableAggregate = 6,

    /// <summary>E-reporting B2C d'export hors UE détaxé (flux 10.3, enchères, TLB1 UNITAIRE, art. 262 I) : une transaction par opération + transmission PA (BUG-11).</summary>
    B2cExportReporting = 7,

    /// <summary>E-reporting B2C d'un document ORDINAIRE taxable (flux 10.3, hors enchères — facture client/TLB1, note d'honoraires/TPS1, F03 §2.9) : agrégation N→1 jour×devise×taux par catégorie + transmission PA (#7).</summary>
    B2cPlainTaxableAggregate = 8,
}
