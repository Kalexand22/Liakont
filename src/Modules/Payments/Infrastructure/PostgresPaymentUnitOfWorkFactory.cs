namespace Liakont.Modules.Payments.Infrastructure;

using Liakont.Modules.Payments.Application;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Fabrique d'unités de travail Payments pour le tenant COURANT : la connexion scopée
/// (<see cref="IConnectionFactory"/>) route vers la base du tenant résolu (<c>ITenantContext</c>).
/// </summary>
internal sealed class PostgresPaymentUnitOfWorkFactory : IPaymentUnitOfWorkFactory
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresPaymentUnitOfWorkFactory(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IPaymentUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
    {
        return await PostgresPaymentUnitOfWork.BeginAsync(_connectionFactory, cancellationToken);
    }
}
