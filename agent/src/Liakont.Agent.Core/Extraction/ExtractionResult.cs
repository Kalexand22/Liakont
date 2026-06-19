namespace Liakont.Agent.Core.Extraction;

/// <summary>Compteurs d'un cycle d'extraction (run), pour la journalisation et le heartbeat (AGT03).</summary>
public sealed class ExtractionResult
{
    /// <summary>Crée un résultat de cycle d'extraction.</summary>
    /// <param name="documentsEnqueued">Documents nouvellement enfilés.</param>
    /// <param name="documentsSkipped">Documents ignorés car déjà acquittés (anti re-push).</param>
    /// <param name="linkedPdfsEnqueued">PDF liés nouvellement enfilés.</param>
    /// <param name="poolPdfsEnqueued">PDF de pool nouvellement enfilés.</param>
    /// <param name="sourceTaxRegimesCollected">Régimes de TVA source collectés.</param>
    /// <param name="documentsQuarantined">
    /// Documents NON conformes au contrat (sérialisation canonique impossible — RDL01) mis en quarantaine,
    /// jamais transmis. ÉVÉNEMENT DE CONFORMITÉ (geste opérateur requis), distinct des skips anti-re-push.
    /// </param>
    public ExtractionResult(
        int documentsEnqueued,
        int documentsSkipped,
        int linkedPdfsEnqueued,
        int poolPdfsEnqueued,
        int sourceTaxRegimesCollected,
        int documentsQuarantined = 0)
    {
        DocumentsEnqueued = documentsEnqueued;
        DocumentsSkipped = documentsSkipped;
        LinkedPdfsEnqueued = linkedPdfsEnqueued;
        PoolPdfsEnqueued = poolPdfsEnqueued;
        SourceTaxRegimesCollected = sourceTaxRegimesCollected;
        DocumentsQuarantined = documentsQuarantined;
    }

    /// <summary>Documents nouvellement enfilés.</summary>
    public int DocumentsEnqueued { get; }

    /// <summary>Documents ignorés car déjà acquittés (anti re-push).</summary>
    public int DocumentsSkipped { get; }

    /// <summary>
    /// Documents NON conformes mis en QUARANTAINE (sérialisation canonique impossible — RDL01), jamais
    /// transmis. Événement de conformité distinct d'un skip anti-re-push (doublon bénin) : à surveiller.
    /// </summary>
    public int DocumentsQuarantined { get; }

    /// <summary>PDF liés nouvellement enfilés.</summary>
    public int LinkedPdfsEnqueued { get; }

    /// <summary>PDF de pool nouvellement enfilés.</summary>
    public int PoolPdfsEnqueued { get; }

    /// <summary>Régimes de TVA source collectés.</summary>
    public int SourceTaxRegimesCollected { get; }
}
