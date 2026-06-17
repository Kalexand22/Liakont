namespace Liakont.Modules.DocumentApproval.Tests.Integration;

using Liakont.Modules.DocumentApproval.Application;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Contracts.Queries;
using Liakont.Modules.DocumentApproval.Infrastructure;
using Liakont.Modules.DocumentApproval.Infrastructure.Queries;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Assemble les vraies dépendances de persistance du module (UoW Postgres + requêtes) sur une fabrique de
/// connexion donnée. Les tests pilotent directement l'unité de travail avec des <c>company_id</c> explicites
/// (le tenant-scoping est prouvé sur ≥ 2 sociétés et ≥ 2 bases).
/// </summary>
internal sealed class DocumentApprovalHarness
{
    public DocumentApprovalHarness(IConnectionFactory connectionFactory)
    {
        ConnectionFactory = connectionFactory;
        UowFactory = new PostgresDocumentValidationUnitOfWorkFactory(connectionFactory);
        Queries = new PostgresDocumentApprovalQueries(connectionFactory);
        Gate = new DocumentApprovalGate(connectionFactory);
        Requirements = new PostgresDocumentApprovalRequirements(connectionFactory);
    }

    public IConnectionFactory ConnectionFactory { get; }

    public IDocumentValidationUnitOfWorkFactory UowFactory { get; }

    public IDocumentApprovalQueries Queries { get; }

    /// <summary>Câblage SIG06 de la Règle de gate (ADR-0028 §5) sur la base de ce tenant.</summary>
    public IDocumentApprovalGate Gate { get; }

    /// <summary>Paramétrage tenant du niveau de preuve requis par purpose (SIG06, V005).</summary>
    public IDocumentApprovalRequirements Requirements { get; }
}
