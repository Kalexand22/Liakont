namespace Liakont.Modules.TvaMapping.Infrastructure;

using System.Text.Json;
using Dapper;
using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Npgsql;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Unité de travail Dapper du module TvaMapping. Insère ou édite la table de mapping (en-tête + règles)
/// de façon atomique, scopée par <c>company_id</c> (CLAUDE.md n°9). L'édition (item TVA05) persiste la
/// mutation ET son entrée de journal append-only dans la MÊME transaction (atomicité, item TVA05 §5).
/// La table des règles est du PARAMÉTRAGE (update/delete permis) ; le journal <c>mapping_change_log</c>
/// est append-only (immuabilité garantie par un trigger base, jamais par un chemin de code).
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

    private const string LockTableForUpdateSql = """
        SELECT id
        FROM tvamapping.mapping_tables
        WHERE company_id = @CompanyId
        FOR UPDATE
        """;

    private const string UpdateTableHeaderSql = """
        UPDATE tvamapping.mapping_tables
        SET validated_by = @ValidatedBy,
            validated_date = @ValidatedDate,
            updated_at = @UpdatedAt
        WHERE id = @Id AND company_id = @CompanyId
        """;

    private const string DeleteRulesSql = """
        DELETE FROM tvamapping.mapping_rules WHERE table_id = @TableId
        """;

    private const string InsertChangeLogSql = """
        INSERT INTO tvamapping.mapping_change_log
            (company_id, table_id, mapping_version, change_type, source_regime_code, part,
             before_value, after_value, operator_id, operator_name)
        VALUES
            (@CompanyId, @TableId, @MappingVersion, @ChangeType, @SourceRegimeCode, @Part,
             @BeforeJson::jsonb, @AfterJson::jsonb, @OperatorId, @OperatorName)
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

        await InsertRulesAsync(table, ct);
    }

    public async Task InsertMappingTableAsync(
        MappingTable table,
        IReadOnlyList<MappingChangeLogEntry> changeLog,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(changeLog);

        // En-tête + règles (réutilise le chemin d'insertion ; lève ConflictException si la table existe
        // déjà pour ce tenant), puis journal append-only dans la MÊME transaction (atomicité, item FIX01b).
        await InsertMappingTableAsync(table, ct);

        foreach (var entry in changeLog)
        {
            ArgumentNullException.ThrowIfNull(entry);
            await InsertChangeLogAsync(entry, ct);
        }
    }

    public async Task<MappingTable?> GetForUpdateAsync(Guid companyId, CancellationToken ct = default)
    {
        // Verrou de la ligne d'en-tête : deux éditions concurrentes du même tenant sont sérialisées
        // (la seconde attend la fin de la première transaction). NULL = aucune table paramétrée.
        var lockedId = await _txn.Connection.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(
                LockTableForUpdateSql,
                new { CompanyId = companyId },
                _txn.Transaction,
                cancellationToken: ct));

        if (lockedId is null)
        {
            return null;
        }

        return await TvaMappingMaterializer.LoadByCompanyAsync(_txn.Connection, companyId, _txn.Transaction, ct);
    }

    public async Task SaveMutationAsync(MappingTable table, MappingChangeLogEntry changeLogEntry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(changeLogEntry);

        // 1. En-tête : nouvel état de validation (effacé par une mutation, renseigné par une validation)
        //    + date de modification. Scopé tenant (id + company_id).
        var affected = await _txn.Connection.ExecuteAsync(
            new CommandDefinition(
                UpdateTableHeaderSql,
                new { table.Id, table.CompanyId, table.ValidatedBy, table.ValidatedDate, table.UpdatedAt },
                _txn.Transaction,
                cancellationToken: ct));

        if (affected != 1)
        {
            // Inatteignable tant que GetForUpdateAsync a verrouillé la ligne dans cette transaction ;
            // garde défensive contre une dérive de scoping.
            throw new InvalidOperationException(
                "La table de mapping TVA ciblée par la mutation est introuvable pour cette société.");
        }

        // 2. Règles : réécriture complète depuis l'agrégat (table de PARAMÉTRAGE — pas un journal —
        //    où l'update/delete est permis ; l'agrégat est la source de vérité de l'ordre des règles).
        await _txn.Connection.ExecuteAsync(
            new CommandDefinition(DeleteRulesSql, new { TableId = table.Id }, _txn.Transaction, cancellationToken: ct));
        await InsertRulesAsync(table, ct);

        // 3. Journal APPEND-ONLY, dans la même transaction → atomicité (item TVA05 §5) : un échec ici
        //    annule aussi la mutation (et inversement).
        await InsertChangeLogAsync(changeLogEntry, ct);
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

    private Task<int> InsertChangeLogAsync(MappingChangeLogEntry changeLogEntry, CancellationToken ct)
        => _txn.Connection.ExecuteAsync(
            new CommandDefinition(
                InsertChangeLogSql,
                new
                {
                    changeLogEntry.CompanyId,
                    changeLogEntry.TableId,
                    changeLogEntry.MappingVersion,
                    ChangeType = (int)changeLogEntry.ChangeType,
                    changeLogEntry.SourceRegimeCode,
                    Part = changeLogEntry.Part.HasValue ? (int?)(int)changeLogEntry.Part.Value : null,
                    changeLogEntry.BeforeJson,
                    changeLogEntry.AfterJson,
                    changeLogEntry.OperatorId,
                    changeLogEntry.OperatorName,
                },
                _txn.Transaction,
                cancellationToken: ct));

    private async Task InsertRulesAsync(MappingTable table, CancellationToken ct)
    {
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
