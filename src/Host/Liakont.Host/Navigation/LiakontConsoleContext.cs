namespace Liakont.Host.Navigation;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Reconciliation.Contracts;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Implémentation par circuit de <see cref="ILiakontConsoleContext"/>. Scopée (un état par circuit) ;
/// dérive <see cref="ReconciliationAvailable"/> de la présence du pool de PDF non rattachés du tenant
/// courant et <see cref="ReconciliationPendingCount"/> de sa file de réconciliation (propositions +
/// orphelins). Tenant-scopée (CLAUDE.md n°9) : pool et file sont explicitement bornés au tenant résolu.
/// </summary>
internal sealed class LiakontConsoleContext : ILiakontConsoleContext
{
    private readonly ITenantContext _tenantContext;
    private readonly IIngestedPdfStore _pdfStore;
    private readonly IReconciliationQueries _reconciliationQueries;
    private bool _initialized;

    public LiakontConsoleContext(
        ITenantContext tenantContext,
        IIngestedPdfStore pdfStore,
        IReconciliationQueries reconciliationQueries)
    {
        _tenantContext = tenantContext;
        _pdfStore = pdfStore;
        _reconciliationQueries = reconciliationQueries;
    }

    public bool ReconciliationAvailable { get; private set; }

    public int ReconciliationPendingCount { get; private set; }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        var tenantId = _tenantContext.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            // Pas de tenant résolu (ex. prérendu sans contexte) : on reste sur le défaut (masqué, compteur 0).
            return;
        }

        var pooled = await _pdfStore.ListPooledPdfsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        ReconciliationAvailable = pooled.Count > 0;

        if (!ReconciliationAvailable)
        {
            // Pas de pool : pas de file à compter (et la section est masquée). On évite deux requêtes inutiles.
            return;
        }

        var proposals = await _reconciliationQueries.GetPendingProposalsAsync(cancellationToken).ConfigureAwait(false);
        var orphans = await _reconciliationQueries.GetOrphanPdfsAsync(cancellationToken).ConfigureAwait(false);
        ReconciliationPendingCount = proposals.Count + orphans.Count;
    }
}
