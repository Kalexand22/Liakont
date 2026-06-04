namespace Liakont.Modules.Ingestion.Infrastructure;

using Liakont.Modules.Ingestion.Application;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;

internal sealed class PostgresReceivedDocumentUnitOfWorkFactory : IReceivedDocumentUnitOfWorkFactory
{
    private readonly ISystemConnectionFactory _systemConnectionFactory;
    private readonly IOutboxWriter _outboxWriter;

    public PostgresReceivedDocumentUnitOfWorkFactory(
        ISystemConnectionFactory systemConnectionFactory,
        IOutboxWriter outboxWriter)
    {
        _systemConnectionFactory = systemConnectionFactory;
        _outboxWriter = outboxWriter;
    }

    public async Task<IReceivedDocumentUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
    {
        return await PostgresReceivedDocumentUnitOfWork.BeginAsync(_systemConnectionFactory, _outboxWriter, cancellationToken);
    }
}
