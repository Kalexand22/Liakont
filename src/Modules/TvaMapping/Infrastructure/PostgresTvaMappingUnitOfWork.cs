namespace Liakont.Modules.TvaMapping.Infrastructure;

using System.Text.Json;
using Dapper;
using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Npgsql;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Unité de travail Dapper du module TvaMapping. Insère la table de mapping (en-tête + règles) de
/// façon atomique, scopée par <c>company_id</c> (CLAUDE.md n°9). Persistance de paramétrage : aucun
/// chemin d'update/delete sur une table d'audit (le journal append-only des modifications arrive
/// avec TVA05).
/// </summary>
internal sealed class PostgresTvaMappingUnitOfWork : ITvaMappingUnitOfWork
{
    private const string InsertTableSql = """
        INSERT INTO tvamapping.mapping_tables
            (id, company_id, mapping_version, validated_by, validated_date,
             default_behavior, created_at, updated_at)
        VALUES
            (@Id, @CompanyId, @MappingVersion, @ValidatedBy, @ValidatedDate,
             @DefaultBehavior, @CreatedAt, @UpdatedAt)
        """;

    private const string InsertRuleSql = """
        INSERT INTO tvamapping.mapping_rules
            (table_id, ordinal, source_regime_code, label, part, source_flags,
             category, vatex, note, rate_mode, rate_value)
        VALUES
            (@TableId, @Ordinal, @SourceRegimeCode, @Label, @Part, @SourceFlags::jsonb,
             @Category, @Vatex, @Note, @RateMode, @RateValue)
        """;

    private readonly TransactionScope _txn;

    private PostgresTvaMappingUnitOfWork(TransactionScope txn)
    {
        _txn = txn;
    }

    public static async Task<PostgresTvaMappingUnitOfWork> BeginAsync(
        IConnectionFactory connectionFactory,
        CancellationToken ct = default)
    {
        var txn = await TransactionScope.BeginAsync(connectionFactory, ct);
        return new PostgresTvaMappingUnitOfWork(txn);
    }

    public async Task InsertMappingTableAsync(MappingTable table, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(table);

        await ExecuteWriteAsync(
            new CommandDefinition(
                InsertTableSql,
                new
                {
                    table.Id,
                    table.CompanyId,
                    table.MappingVersion,
                    table.ValidatedBy,
                    table.ValidatedDate,
                    DefaultBehavior = (int)table.DefaultBehavior,
                    table.CreatedAt,
                    table.UpdatedAt,
                },
                _txn.Transaction,
                cancellationToken: ct),
            "Une table de mapping TVA existe déjà pour cette société.");

        for (var ordinal = 0; ordinal < table.Rules.Count; ordinal++)
        {
            var rule = table.Rules[ordinal];
            await ExecuteWriteAsync(
                new CommandDefinition(
                    InsertRuleSql,
                    new
                    {
                        TableId = table.Id,
                        Ordinal = ordinal,
                        rule.SourceRegimeCode,
                        rule.Label,
                        Part = (int)rule.Part,
                        SourceFlags = SerializeFlags(rule.SourceFlags),
                        Category = (int)rule.Category,
                        rule.Vatex,
                        rule.Note,
                        RateMode = (int)rule.RateMode,
                        rule.RateValue,
                    },
                    _txn.Transaction,
                    cancellationToken: ct),
                "Doublon de règle de mapping (code régime et part) pour cette table.");
        }
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _txn.CommitAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _txn.DisposeAsync();
    }

    private static string? SerializeFlags(IReadOnlyDictionary<string, string>? flags)
    {
        return flags is { Count: > 0 } ? JsonSerializer.Serialize(flags) : null;
    }

    private async Task<int> ExecuteWriteAsync(CommandDefinition command, string conflictMessage)
    {
        try
        {
            return await _txn.Connection.ExecuteAsync(command);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ConflictException(conflictMessage, ex);
        }
    }
}
