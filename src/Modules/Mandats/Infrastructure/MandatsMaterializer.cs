namespace Liakont.Modules.Mandats.Infrastructure;

using System.Data;
using Dapper;
using Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// Charge un <see cref="Mandant"/> ou un <see cref="Mandat"/> depuis la base et le reconstitue en entité
/// de domaine. Lecture toujours scopée par <c>company_id</c> (CLAUDE.md n°9, INV-MANDATS-1).
/// </summary>
internal static class MandatsMaterializer
{
    private const string SelectMandantSql = """
        SELECT id, company_id, reference, raison_sociale, seller_vat_number, siren,
               numbering_prefix, created_at, updated_at
        FROM mandats.mandants
        WHERE company_id = @CompanyId AND reference = @Reference
        """;

    private const string SelectMandatSql = """
        SELECT id, company_id, mandant_id, reference, clause_text, est_ecrit, assujettissement_status,
               contestation_delay, validated_by, validated_date, revoked_date, created_at, updated_at
        FROM mandats.mandats
        WHERE company_id = @CompanyId AND mandant_id = @MandantId AND reference = @Reference
        """;

    public static async Task<Mandant?> LoadMandantAsync(
        IDbConnection connection,
        Guid companyId,
        string reference,
        IDbTransaction? transaction,
        CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(
                SelectMandantSql,
                new { CompanyId = companyId, Reference = reference },
                transaction,
                cancellationToken: ct));

        return row is null ? null : MapMandant(row);
    }

    public static async Task<Mandat?> LoadMandatAsync(
        IDbConnection connection,
        Guid companyId,
        Guid mandantId,
        string reference,
        IDbTransaction? transaction,
        CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(
                SelectMandatSql,
                new { CompanyId = companyId, MandantId = mandantId, Reference = reference },
                transaction,
                cancellationToken: ct));

        return row is null ? null : MapMandat(row);
    }

    public static Mandant MapMandant(dynamic row)
    {
        return Mandant.Reconstitute(
            (Guid)row.id,
            (Guid)row.company_id,
            (string)row.reference,
            (string)row.raison_sociale,
            (string?)row.seller_vat_number,
            (string)row.siren,
            (string)row.numbering_prefix,
            MandatsRowReader.ToDateTimeOffset((object)row.created_at),
            MandatsRowReader.ToNullableDateTimeOffset((object?)row.updated_at));
    }

    public static Mandat MapMandat(dynamic row)
    {
        return Mandat.Reconstitute(
            (Guid)row.id,
            (Guid)row.company_id,
            (Guid)row.mandant_id,
            (string)row.reference,
            (string)row.clause_text,
            (bool)row.est_ecrit,
            (string?)row.assujettissement_status,
            MandatsRowReader.ToNullableTimeSpan((object?)row.contestation_delay),
            (string?)row.validated_by,
            MandatsRowReader.ToNullableDateOnly((object?)row.validated_date),
            MandatsRowReader.ToNullableDateTimeOffset((object?)row.revoked_date),
            MandatsRowReader.ToDateTimeOffset((object)row.created_at),
            MandatsRowReader.ToNullableDateTimeOffset((object?)row.updated_at));
    }
}
