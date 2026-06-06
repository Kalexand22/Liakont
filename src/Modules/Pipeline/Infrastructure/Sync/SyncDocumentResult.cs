namespace Liakont.Modules.Pipeline.Infrastructure.Sync;

/// <summary>
/// Issue de la synchronisation d'UN document émis : nombre de factures PA et de tax reports ajoutés en
/// addendum à son paquet WORM ce cycle, et marqueurs d'ignoré / d'échec. Un document peut produire plusieurs
/// addenda (une facture + N tax reports) ; l'agrégation se fait dans <see cref="SyncTally"/>.
/// </summary>
internal readonly record struct SyncDocumentResult(int InvoicesArchived, int TaxReportsArchived, bool Skipped, bool Failed)
{
    /// <summary>Document ignoré (état changé entre le snapshot et le traitement, ou référence PA absente).</summary>
    public static readonly SyncDocumentResult SkippedResult = new(0, 0, Skipped: true, Failed: false);

    /// <summary>Échec inattendu sur ce document — document ignoré ce cycle, traitement du tenant poursuivi.</summary>
    public static readonly SyncDocumentResult FailedResult = new(0, 0, Skipped: false, Failed: true);
}
