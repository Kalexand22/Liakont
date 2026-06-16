namespace Liakont.Modules.Mandats.Infrastructure.Queries;

using Dapper;
using Liakont.Modules.Mandats.Contracts.DTOs;
using Liakont.Modules.Mandats.Contracts.Queries;
using Liakont.Modules.Mandats.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Requêtes de lecture (seules) du module Mandats sur PostgreSQL. Toutes scopées par <c>company_id</c>
/// (CLAUDE.md n°9/17, INV-MANDATS-1) — aucune lecture cross-tenant. Le mapping passe par les agrégats de
/// domaine (re-construits via <see cref="MandatsMaterializer"/>) pour exposer l'état calculé
/// (<see cref="Mandat.IsSelfBillingSuspended"/>) sans dupliquer la règle.
/// </summary>
internal sealed class PostgresMandatsQueries : IMandatsQueries
{
    private const string SelectMandantsSql = """
        SELECT id, company_id, reference, raison_sociale, seller_vat_number, siren,
               numbering_prefix, created_at, updated_at
        FROM mandats.mandants
        WHERE company_id = @CompanyId
        ORDER BY reference ASC
        """;

    private const string SelectMandatsSql = """
        SELECT id, company_id, mandant_id, reference, clause_text, est_ecrit, assujettissement_status,
               contestation_delay, validated_by, validated_date, revoked_date, created_at, updated_at
        FROM mandats.mandats
        WHERE company_id = @CompanyId AND mandant_id = @MandantId
        ORDER BY reference ASC
        """;

    private const string SelectChangeLogSql = """
        SELECT mandant_id, mandat_id, reference, change_type, before_value, after_value,
               operator_id, operator_name, occurred_at
        FROM mandats.mandat_change_log
        WHERE company_id = @CompanyId
        ORDER BY occurred_at DESC
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresMandatsQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<MandantDto?> GetMandant(Guid companyId, string reference, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenAsync(ct);
        var mandant = await MandatsMaterializer.LoadMandantAsync(connection, companyId, reference, null, ct);
        return mandant is null ? null : MapMandant(mandant);
    }

    public async Task<IReadOnlyList<MandantDto>> ListMandants(Guid companyId, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync(
            new CommandDefinition(SelectMandantsSql, new { CompanyId = companyId }, cancellationToken: ct));

        var result = new List<MandantDto>();
        foreach (var row in rows)
        {
            result.Add(MapMandant(MandatsMaterializer.MapMandant(row)));
        }

        return result;
    }

    public async Task<MandatDto?> GetMandat(Guid companyId, Guid mandantId, string reference, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenAsync(ct);
        var mandat = await MandatsMaterializer.LoadMandatAsync(connection, companyId, mandantId, reference, null, ct);
        return mandat is null ? null : MapMandat(mandat);
    }

    public async Task<IReadOnlyList<MandatDto>> ListMandats(Guid companyId, Guid mandantId, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync(
            new CommandDefinition(SelectMandatsSql, new { CompanyId = companyId, MandantId = mandantId }, cancellationToken: ct));

        var result = new List<MandatDto>();
        foreach (var row in rows)
        {
            result.Add(MapMandat(MandatsMaterializer.MapMandat(row)));
        }

        return result;
    }

    public async Task<IReadOnlyList<MandatChangeLogEntryDto>> GetChangeLog(Guid companyId, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync(
            new CommandDefinition(SelectChangeLogSql, new { CompanyId = companyId }, cancellationToken: ct));

        var result = new List<MandatChangeLogEntryDto>();
        foreach (var row in rows)
        {
            result.Add(new MandatChangeLogEntryDto
            {
                MandantId = (Guid)row.mandant_id,
                MandatId = (Guid?)row.mandat_id,
                Reference = (string)row.reference,
                ChangeType = ((MandatChangeType)(int)row.change_type).ToString(),
                BeforeValue = (string?)row.before_value,
                AfterValue = (string?)row.after_value,
                OperatorId = (Guid)row.operator_id,
                OperatorName = (string?)row.operator_name,
                OccurredAt = MandatsRowReader.ToDateTimeOffset((object)row.occurred_at),
            });
        }

        return result;
    }

    private static MandantDto MapMandant(Mandant mandant)
        => new()
        {
            Id = mandant.Id,
            Reference = mandant.Reference,
            RaisonSociale = mandant.RaisonSociale,
            SellerVatNumber = mandant.SellerVatNumber,
            Siren = mandant.Siren,
            NumberingPrefix = mandant.NumberingPrefix,
        };

    private static MandatDto MapMandat(Mandat mandat)
        => new()
        {
            Id = mandat.Id,
            MandantId = mandat.MandantId,
            Reference = mandat.Reference,
            ClauseText = mandat.ClauseText,
            EstEcrit = mandat.EstEcrit,
            AssujettissementStatus = mandat.AssujettissementStatus,
            ContestationDelay = mandat.ContestationDelay,
            ValidatedBy = mandat.ValidatedBy,
            ValidatedDate = mandat.ValidatedDate,
            RevokedDate = mandat.RevokedDate,
            IsValidated = mandat.IsValidated,
            IsRevoked = mandat.IsRevoked,
            IsSelfBillingSuspended = mandat.IsSelfBillingSuspended,
        };
}
