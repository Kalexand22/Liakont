namespace Liakont.Modules.DocumentApproval.Infrastructure;

using Liakont.Modules.DocumentApproval.Application;
using Stratum.Common.Infrastructure.Database;

internal sealed class PostgresDocumentValidationUnitOfWorkFactory : IDocumentValidationUnitOfWorkFactory
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresDocumentValidationUnitOfWorkFactory(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IDocumentValidationUnitOfWork> BeginAsync(CancellationToken ct = default)
    {
        return await PostgresDocumentValidationUnitOfWork.BeginAsync(_connectionFactory, ct);
    }
}
