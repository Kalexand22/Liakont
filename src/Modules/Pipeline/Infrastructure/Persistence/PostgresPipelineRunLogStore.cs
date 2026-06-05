namespace Liakont.Modules.Pipeline.Infrastructure.Persistence;

using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Domain;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Écriture Dapper du journal d'exécutions du pipeline (<c>pipeline.run_logs</c>) sur la base DU TENANT
/// courant (<see cref="IConnectionFactory"/> route vers le tenant résolu — database-per-tenant,
/// blueprint §7). La nature et l'origine sont stockées par NOM d'énumération (symétrique de la lecture
/// <see cref="Queries.PostgresPipelineRunQueries"/> qui les <c>Enum.Parse</c>). C'est une table de
/// JOURNAL d'exécution (PIP01), NI une table d'audit NI un coffre WORM : aucune contrainte append-only
/// produit ne s'y applique (CLAUDE.md n°4 inchangé).
/// </summary>
public sealed class PostgresPipelineRunLogStore : IPipelineRunLogStore
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresPipelineRunLogStore(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveAsync(RunLog runLog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runLog);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO pipeline.run_logs
                (id, run_type, run_trigger, started_at, completed_at,
                 documents_processed, documents_succeeded, documents_failed, detail)
            VALUES
                (@Id, @RunType, @Trigger, @StartedAt, @CompletedAt,
                 @DocumentsProcessed, @DocumentsSucceeded, @DocumentsFailed, @Detail)
            """;

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                runLog.Id,
                RunType = runLog.RunType.ToString(),
                Trigger = runLog.Trigger.ToString(),
                runLog.StartedAt,
                runLog.CompletedAt,
                runLog.DocumentsProcessed,
                runLog.DocumentsSucceeded,
                runLog.DocumentsFailed,
                runLog.Detail,
            },
            cancellationToken: cancellationToken));
    }
}
