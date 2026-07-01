namespace Liakont.Modules.Ged.Infrastructure.Ingestion;

using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Adapte <see cref="ISystemConnectionFactory"/> (base système partagée) à <see cref="IConnectionFactory"/> pour
/// réutiliser <see cref="TransactionScope"/> — copie GED-LOCALE (le canal GED ne référence JAMAIS
/// <c>Ingestion.Infrastructure</c>, frontière de module F19 §6/RL-01). Le registre de réception GED
/// (<c>ged_ingestion.ged_received_documents</c>) et l'outbox vivent dans la base SYSTÈME (F19 §3.2 (a), RL-03) : c'est
/// la seule façon d'écrire ATOMIQUEMENT le registre + l'événement <c>ManagedDocumentReceivedV1</c>.
/// </summary>
internal sealed class GedSystemConnectionFactoryAdapter : IConnectionFactory
{
    private readonly ISystemConnectionFactory _systemConnectionFactory;

    public GedSystemConnectionFactoryAdapter(ISystemConnectionFactory systemConnectionFactory)
    {
        _systemConnectionFactory = systemConnectionFactory;
    }

    public Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default) =>
        _systemConnectionFactory.OpenAsync(cancellationToken);
}
