namespace Liakont.Modules.Documents.Infrastructure;

using System.Data;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Adapte <see cref="ITenantConnectionFactory"/> + un slug de tenant FIXE à <see cref="IConnectionFactory"/>,
/// pour réutiliser <see cref="TransactionScope"/> sur la base d'un tenant DONNÉ (item TRK01).
/// Nécessaire au câblage de l'ingestion (port <c>IDocumentIntake</c>) : la création du document
/// intervient sur un endpoint de niveau SYSTÈME (la résolution clé API -> tenant précède tout contexte
/// tenant, F12 §3.1), donc le tenant est connu par le SLUG transmis et non par <see cref="ITenantContext"/>.
/// Même motif que <c>SystemConnectionFactoryAdapter</c> côté Ingestion.
/// </summary>
internal sealed class TenantSlugConnectionFactoryAdapter : IConnectionFactory
{
    private readonly ITenantConnectionFactory _tenantConnectionFactory;
    private readonly string _tenantId;

    public TenantSlugConnectionFactoryAdapter(ITenantConnectionFactory tenantConnectionFactory, string tenantId)
    {
        _tenantConnectionFactory = tenantConnectionFactory;
        _tenantId = tenantId;
    }

    public Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default) =>
        _tenantConnectionFactory.OpenAsync(_tenantId, cancellationToken);
}
