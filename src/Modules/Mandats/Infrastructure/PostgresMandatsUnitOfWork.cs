namespace Liakont.Modules.Mandats.Infrastructure;

using Dapper;
using Liakont.Modules.Mandats.Application;
using Liakont.Modules.Mandats.Domain.Entities;
using Npgsql;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Unité de travail Dapper du module Mandats. Persiste registre (mandants) et cycle de vie (mandats) de
/// façon atomique, scopée par <c>company_id</c> (CLAUDE.md n°9, INV-MANDATS-1). Chaque mutation écrit
/// l'agrégat ET son entrée de journal append-only (<c>mandat_change_log</c>) dans la MÊME transaction
/// (ADR-0022 §3 / INV-MANDATS-3 : « pas de mutation sans ligne de journal »). Les tables
/// <c>mandants</c>/<c>mandats</c> sont du PARAMÉTRAGE (update permis) ; le journal est append-only
/// (immuabilité garantie par un trigger base, jamais par un chemin de code).
/// </summary>
internal sealed class PostgresMandatsUnitOfWork : IMandatsUnitOfWork
{
    private const string InsertMandantSql = """
        INSERT INTO mandats.mandants
            (id, company_id, reference, raison_sociale, seller_vat_number, siren,
             numbering_prefix, created_at, updated_at)
        VALUES
            (@Id, @CompanyId, @Reference, @RaisonSociale, @SellerVatNumber, @Siren,
             @NumberingPrefix, @CreatedAt, @UpdatedAt)
        """;

    private const string LockMandantForUpdateSql = """
        SELECT id
        FROM mandats.mandants
        WHERE company_id = @CompanyId AND reference = @Reference
        FOR UPDATE
        """;

    private const string UpdateMandantSql = """
        UPDATE mandats.mandants
        SET raison_sociale = @RaisonSociale,
            seller_vat_number = @SellerVatNumber,
            siren = @Siren,
            numbering_prefix = @NumberingPrefix,
            updated_at = @UpdatedAt
        WHERE id = @Id AND company_id = @CompanyId
        """;

    private const string InsertMandatSql = """
        INSERT INTO mandats.mandats
            (id, company_id, mandant_id, reference, clause_text, est_ecrit, assujettissement_status,
             contestation_delay, validated_by, validated_date, revoked_date, created_at, updated_at)
        VALUES
            (@Id, @CompanyId, @MandantId, @Reference, @ClauseText, @EstEcrit, @AssujettissementStatus,
             @ContestationDelay, @ValidatedBy, @ValidatedDate, @RevokedDate, @CreatedAt, @UpdatedAt)
        """;

    private const string LockMandatForUpdateSql = """
        SELECT id
        FROM mandats.mandats
        WHERE company_id = @CompanyId AND mandant_id = @MandantId AND reference = @Reference
        FOR UPDATE
        """;

    private const string UpdateMandatSql = """
        UPDATE mandats.mandats
        SET clause_text = @ClauseText,
            est_ecrit = @EstEcrit,
            assujettissement_status = @AssujettissementStatus,
            contestation_delay = @ContestationDelay,
            validated_by = @ValidatedBy,
            validated_date = @ValidatedDate,
            revoked_date = @RevokedDate,
            updated_at = @UpdatedAt
        WHERE id = @Id AND company_id = @CompanyId
        """;

    private const string InsertChangeLogSql = """
        INSERT INTO mandats.mandat_change_log
            (company_id, mandant_id, mandat_id, reference, change_type,
             before_value, after_value, operator_id, operator_name)
        VALUES
            (@CompanyId, @MandantId, @MandatId, @Reference, @ChangeType,
             @BeforeJson::jsonb, @AfterJson::jsonb, @OperatorId, @OperatorName)
        """;

    private readonly TransactionScope _txn;

    private PostgresMandatsUnitOfWork(TransactionScope txn)
    {
        _txn = txn;
    }

    public static async Task<PostgresMandatsUnitOfWork> BeginAsync(
        IConnectionFactory connectionFactory,
        CancellationToken ct = default)
    {
        var txn = await TransactionScope.BeginAsync(connectionFactory, ct);
        return new PostgresMandatsUnitOfWork(txn);
    }

    public async Task InsertMandantAsync(Mandant mandant, MandatChangeLogEntry changeLogEntry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mandant);
        ArgumentNullException.ThrowIfNull(changeLogEntry);

        await ExecuteWriteAsync(
            new CommandDefinition(
                InsertMandantSql,
                new
                {
                    mandant.Id,
                    mandant.CompanyId,
                    mandant.Reference,
                    mandant.RaisonSociale,
                    mandant.SellerVatNumber,
                    mandant.Siren,
                    mandant.NumberingPrefix,
                    mandant.CreatedAt,
                    mandant.UpdatedAt,
                },
                _txn.Transaction,
                cancellationToken: ct),
            "Un mandant de même référence existe déjà pour cette société.");

        await InsertChangeLogAsync(changeLogEntry, ct);
    }

    public async Task<Mandant?> GetMandantForUpdateAsync(Guid companyId, string reference, CancellationToken ct = default)
    {
        var lockedId = await _txn.Connection.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(
                LockMandantForUpdateSql,
                new { CompanyId = companyId, Reference = reference },
                _txn.Transaction,
                cancellationToken: ct));

        if (lockedId is null)
        {
            return null;
        }

        return await MandatsMaterializer.LoadMandantAsync(_txn.Connection, companyId, reference, _txn.Transaction, ct);
    }

    public async Task SaveMandantMutationAsync(Mandant mandant, MandatChangeLogEntry changeLogEntry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mandant);
        ArgumentNullException.ThrowIfNull(changeLogEntry);

        var affected = await _txn.Connection.ExecuteAsync(
            new CommandDefinition(
                UpdateMandantSql,
                new
                {
                    mandant.Id,
                    mandant.CompanyId,
                    mandant.RaisonSociale,
                    mandant.SellerVatNumber,
                    mandant.Siren,
                    mandant.NumberingPrefix,
                    mandant.UpdatedAt,
                },
                _txn.Transaction,
                cancellationToken: ct));

        if (affected != 1)
        {
            throw new InvalidOperationException(
                "Le mandant ciblé par la mutation est introuvable pour cette société.");
        }

        await InsertChangeLogAsync(changeLogEntry, ct);
    }

    public async Task InsertMandatAsync(Mandat mandat, MandatChangeLogEntry changeLogEntry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mandat);
        ArgumentNullException.ThrowIfNull(changeLogEntry);

        await ExecuteWriteAsync(
            new CommandDefinition(
                InsertMandatSql,
                MandatParameters(mandat),
                _txn.Transaction,
                cancellationToken: ct),
            "Un mandat de même référence existe déjà pour ce mandant.");

        await InsertChangeLogAsync(changeLogEntry, ct);
    }

    public async Task<Mandat?> GetMandatForUpdateAsync(Guid companyId, Guid mandantId, string reference, CancellationToken ct = default)
    {
        var lockedId = await _txn.Connection.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(
                LockMandatForUpdateSql,
                new { CompanyId = companyId, MandantId = mandantId, Reference = reference },
                _txn.Transaction,
                cancellationToken: ct));

        if (lockedId is null)
        {
            return null;
        }

        return await MandatsMaterializer.LoadMandatAsync(_txn.Connection, companyId, mandantId, reference, _txn.Transaction, ct);
    }

    public async Task SaveMandatMutationAsync(Mandat mandat, MandatChangeLogEntry changeLogEntry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mandat);
        ArgumentNullException.ThrowIfNull(changeLogEntry);

        var affected = await _txn.Connection.ExecuteAsync(
            new CommandDefinition(
                UpdateMandatSql,
                MandatParameters(mandat),
                _txn.Transaction,
                cancellationToken: ct));

        if (affected != 1)
        {
            throw new InvalidOperationException(
                "Le mandat ciblé par la mutation est introuvable pour cette société.");
        }

        // Journal APPEND-ONLY, dans la même transaction → atomicité (ADR-0022 §3) : un échec ici annule
        // aussi la mutation (et inversement).
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

    private static object MandatParameters(Mandat mandat)
        => new
        {
            mandat.Id,
            mandat.CompanyId,
            mandat.MandantId,
            mandat.Reference,
            mandat.ClauseText,
            mandat.EstEcrit,
            mandat.AssujettissementStatus,
            mandat.ContestationDelay,
            mandat.ValidatedBy,
            mandat.ValidatedDate,
            mandat.RevokedDate,
            mandat.CreatedAt,
            mandat.UpdatedAt,
        };

    private Task<int> InsertChangeLogAsync(MandatChangeLogEntry entry, CancellationToken ct)
        => _txn.Connection.ExecuteAsync(
            new CommandDefinition(
                InsertChangeLogSql,
                new
                {
                    entry.CompanyId,
                    entry.MandantId,
                    entry.MandatId,
                    entry.Reference,
                    ChangeType = (int)entry.ChangeType,
                    entry.BeforeJson,
                    entry.AfterJson,
                    entry.OperatorId,
                    entry.OperatorName,
                },
                _txn.Transaction,
                cancellationToken: ct));

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
