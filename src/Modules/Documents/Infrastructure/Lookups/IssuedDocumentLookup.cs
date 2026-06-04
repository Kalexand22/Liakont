namespace Liakont.Modules.Documents.Infrastructure.Lookups;

using Dapper;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Validation.Contracts;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Implémentation réelle (item TRK03) du port <see cref="IIssuedDocumentLookup"/> déclaré par le module
/// Validation (VAL03, anti-doublon de numéro F04 §3.3) : un numéro déjà émis pour le tenant fait échouer
/// la règle d'unicité. Lecture sur la base DU TENANT courant (<see cref="IConnectionFactory"/> — la
/// connexion EST le tenant, database-per-tenant blueprint §7) : l'isolation est ASSURÉE PAR LA CONNEXION,
/// pas par une colonne ; <paramref name="companyId"/> identifie le tenant déjà honoré par la connexion
/// scopée (aucune requête cross-tenant possible — CLAUDE.md n°9/17).
/// </summary>
public sealed class IssuedDocumentLookup : IIssuedDocumentLookup
{
    private const string ExistsIssuedByNumberSql = """
        SELECT EXISTS(
            SELECT 1
            FROM documents.documents
            WHERE document_number = @DocumentNumber
              AND state = @IssuedState)
        """;

    private readonly IConnectionFactory _connectionFactory;

    public IssuedDocumentLookup(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> IsAlreadyIssuedAsync(Guid companyId, string documentNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentNumber))
        {
            return false;
        }

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            ExistsIssuedByNumberSql,
            new { DocumentNumber = documentNumber, IssuedState = nameof(DocumentState.Issued) },
            cancellationToken: cancellationToken));
    }
}
