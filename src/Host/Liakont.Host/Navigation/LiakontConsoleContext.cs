namespace Liakont.Host.Navigation;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Security;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Reconciliation.Contracts;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Implémentation par circuit de <see cref="ILiakontConsoleContext"/>. Scopée (un état par circuit) ;
/// dérive <see cref="ReconciliationAvailable"/> de la présence du pool de PDF non rattachés du tenant
/// courant et <see cref="ReconciliationPendingCount"/> de sa file de réconciliation (propositions +
/// orphelins). Tenant-scopée (CLAUDE.md n°9) : pool et file sont explicitement bornés au tenant résolu.
/// </summary>
internal sealed partial class LiakontConsoleContext : ILiakontConsoleContext
{
    private readonly ITenantContext _tenantContext;
    private readonly IIngestedPdfStore _pdfStore;
    private readonly IReconciliationQueries _reconciliationQueries;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ILogger<LiakontConsoleContext> _logger;
    private bool _initialized;

    public LiakontConsoleContext(
        ITenantContext tenantContext,
        IIngestedPdfStore pdfStore,
        IReconciliationQueries reconciliationQueries,
        AuthenticationStateProvider authStateProvider,
        ILogger<LiakontConsoleContext> logger)
    {
        _tenantContext = tenantContext;
        _pdfStore = pdfStore;
        _reconciliationQueries = reconciliationQueries;
        _authStateProvider = authStateProvider;
        _logger = logger;
    }

    public bool ReconciliationAvailable { get; private set; }

    public int ReconciliationPendingCount { get; private set; }

    public bool IsCrossTenantAdmin { get; private set; }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        // RB1 — un super-admin (stratum-admin) opère en CROSS-TENANT : il n'appartient à aucun tenant.
        // On le détecte une fois à l'ouverture du circuit ; la nav masque alors les surfaces tenant-scopées
        // et le chrome n'affiche pas de tenant courant. Un hoquet de lecture de l'état d'auth dégrade à
        // « pas super-admin » (deny-by-default sur le masquage : on n'élargit jamais l'accès par erreur).
        try
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
            IsCrossTenantAdmin = SuperAdminRoles.IsSuperAdmin(authState.User);
        }
        catch
        {
            IsCrossTenantAdmin = false;
        }

        if (IsCrossTenantAdmin)
        {
            // Cross-tenant : aucune surface tenant n'est rendue (la nav les masque) → rien à pré-charger.
            return;
        }

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

        try
        {
            var proposals = await _reconciliationQueries.GetPendingProposalsAsync(cancellationToken).ConfigureAwait(false);
            var orphans = await _reconciliationQueries.GetOrphanPdfsAsync(cancellationToken).ConfigureAwait(false);
            ReconciliationPendingCount = proposals.Count + orphans.Count;
        }
        catch (Exception ex)
        {
            // Le compteur est PUREMENT DÉCORATIF (badge de nav) : un hoquet de lecture de la file ne doit pas
            // faire échouer l'ouverture du circuit — sinon toute la console devient indisponible pour un tenant
            // qui a un pool. On dégrade à 0 (badge masqué) et on trace, sans propager.
            ReconciliationPendingCount = 0;
            LogPendingCountFailed(_logger, ex);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to compute the reconciliation pending count for the navigation badge; degrading to 0.")]
    private static partial void LogPendingCountFailed(ILogger logger, Exception exception);
}
