namespace Liakont.Modules.Ged.Infrastructure.Ingestion;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Application.Ingestion;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;

/// <summary>
/// Fabrique l'<see cref="IGedReceivedDocumentUnitOfWork"/> Postgres sur la base SYSTÈME (<see cref="ISystemConnectionFactory"/>)
/// co-localisée avec l'outbox (<see cref="IOutboxWriter"/>) : chaque <see cref="BeginAsync"/> ouvre une connexion +
/// transaction fraîches où le registre GED ET l'événement s'écrivent atomiquement (RL-03).
/// </summary>
internal sealed class PostgresGedReceivedDocumentUnitOfWorkFactory : IGedReceivedDocumentUnitOfWorkFactory
{
    private readonly ISystemConnectionFactory _systemConnectionFactory;
    private readonly IOutboxWriter _outboxWriter;

    public PostgresGedReceivedDocumentUnitOfWorkFactory(
        ISystemConnectionFactory systemConnectionFactory,
        IOutboxWriter outboxWriter)
    {
        _systemConnectionFactory = systemConnectionFactory;
        _outboxWriter = outboxWriter;
    }

    public async Task<IGedReceivedDocumentUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
    {
        return await PostgresGedReceivedDocumentUnitOfWork.BeginAsync(_systemConnectionFactory, _outboxWriter, cancellationToken);
    }
}
