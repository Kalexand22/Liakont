namespace Liakont.Modules.Documents.Infrastructure;

using Liakont.Modules.Documents.Application;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Fabrique d'unités de travail Documents pour le tenant COURANT : la connexion scopée
/// (<see cref="IConnectionFactory"/>) route vers la base du tenant résolu (<c>ITenantContext</c>).
/// Le câblage de l'ingestion (port <c>IDocumentIntake</c>), lui, cible un tenant par SLUG explicite
/// (voir <see cref="DocumentIntake"/>) car il précède tout contexte tenant.
/// </summary>
internal sealed class PostgresDocumentUnitOfWorkFactory : IDocumentUnitOfWorkFactory
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresDocumentUnitOfWorkFactory(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IDocumentUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
    {
        return await PostgresDocumentUnitOfWork.BeginAsync(_connectionFactory, cancellationToken);
    }
}
