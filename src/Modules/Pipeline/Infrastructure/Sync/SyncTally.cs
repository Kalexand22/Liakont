namespace Liakont.Modules.Pipeline.Infrastructure.Sync;

using System.Globalization;

/// <summary>Compteurs d'une exécution SYNC, consignés dans <c>pipeline.run_logs</c>.</summary>
internal sealed class SyncTally
{
    /// <summary>Documents émis examinés ce cycle.</summary>
    public int Processed { get; private set; }

    /// <summary>Documents ignorés (état changé, référence PA absente).</summary>
    public int Skipped { get; private set; }

    /// <summary>Documents en échec inattendu (ignorés ce cycle, traitement poursuivi).</summary>
    public int Failed { get; private set; }

    /// <summary>Factures électroniques générées par la PA ajoutées en addendum.</summary>
    public int Invoices { get; private set; }

    /// <summary>Tax reports DGFiP ajoutés en addendum.</summary>
    public int TaxReports { get; private set; }

    /// <summary>Documents traités sans échec (pris en compte par le journal d'exécutions).</summary>
    public int Succeeded => Processed - Failed;

    /// <summary>Comptabilise l'issue d'un document.</summary>
    public void Add(SyncDocumentResult result)
    {
        Processed++;
        Invoices += result.InvoicesArchived;
        TaxReports += result.TaxReportsArchived;

        if (result.Failed)
        {
            Failed++;
        }
        else if (result.Skipped)
        {
            Skipped++;
        }
    }

    /// <summary>Détail lisible de l'exécution pour le journal d'exécutions.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"SYNC : {Processed} document(s) émis examiné(s), {Invoices} facture(s) PA et {TaxReports} tax report(s) archivé(s) en addendum, {Skipped} ignoré(s), {Failed} en échec.");
}
