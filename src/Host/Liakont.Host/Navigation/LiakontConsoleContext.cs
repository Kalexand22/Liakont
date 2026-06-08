namespace Liakont.Host.Navigation;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ingestion.Contracts;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Implémentation par circuit de <see cref="ILiakontConsoleContext"/>. Scopée (un état par circuit) ;
/// dérive <see cref="ReconciliationAvailable"/> de la présence du pool de PDF non rattachés du tenant
/// courant. Tenant-scopée (CLAUDE.md n°9) : la lecture du pool est explicitement bornée au tenant résolu.
/// </summary>
internal sealed class LiakontConsoleContext : ILiakontConsoleContext
{
    private readonly ITenantContext _tenantContext;
    private readonly IIngestedPdfStore _pdfStore;
    private bool _initialized;

    public LiakontConsoleContext(ITenantContext tenantContext, IIngestedPdfStore pdfStore)
    {
        _tenantContext = tenantContext;
        _pdfStore = pdfStore;
    }

    public bool ReconciliationAvailable { get; private set; }

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
            // Pas de tenant résolu (ex. prérendu sans contexte) : on reste sur le défaut (masqué).
            return;
        }

        var pooled = await _pdfStore.ListPooledPdfsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        ReconciliationAvailable = pooled.Count > 0;
    }
}
