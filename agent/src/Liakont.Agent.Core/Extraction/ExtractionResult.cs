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
    public ExtractionResult(
        int documentsEnqueued,
        int documentsSkipped,
        int linkedPdfsEnqueued,
        int poolPdfsEnqueued,
        int sourceTaxRegimesCollected)
    {
        DocumentsEnqueued = documentsEnqueued;
        DocumentsSkipped = documentsSkipped;
        LinkedPdfsEnqueued = linkedPdfsEnqueued;
        PoolPdfsEnqueued = poolPdfsEnqueued;
        SourceTaxRegimesCollected = sourceTaxRegimesCollected;
    }

    /// <summary>Documents nouvellement enfilés.</summary>
    public int DocumentsEnqueued { get; }

    /// <summary>Documents ignorés car déjà acquittés (anti re-push).</summary>
    public int DocumentsSkipped { get; }

    /// <summary>PDF liés nouvellement enfilés.</summary>
    public int LinkedPdfsEnqueued { get; }

    /// <summary>PDF de pool nouvellement enfilés.</summary>
    public int PoolPdfsEnqueued { get; }

    /// <summary>Régimes de TVA source collectés.</summary>
    public int SourceTaxRegimesCollected { get; }
}
