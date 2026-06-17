namespace Liakont.Modules.Signature.Infrastructure.OnSite;

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Signature.Application.OnSite;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Registre des liaisons VÉRIFIÉES déposant→signataire sur Dapper/PostgreSQL (ADR-0030 §5 ; INV-ONSITE-7).
/// APPEND-ONLY (registre d'identité immuable, double trigger base) ; tenant-scopé par construction (connexion
/// = base du tenant — CLAUDE.md n°9). La résolution lit la liaison la PLUS RÉCENTE (seq) du document : c'est la
/// seule source d'un signataire probant, jamais le payload de capture (test d'usurpation).
/// </summary>
internal sealed class PostgresOnSiteSignerBindingStore : IOnSiteSignerBindingStore
{
    private const string InsertSql = """
        INSERT INTO signature.onsite_signer_bindings
            (id, company_id, document_id, signer_identity, verification_method, registered_by, verified_at)
        VALUES
            (@Id, @CompanyId, @DocumentId, @SignerIdentity, @VerificationMethod, @RegisteredBy, @VerifiedAt)
        """;

    private const string ResolveLatestSql = """
        SELECT id, company_id, document_id, signer_identity, verification_method, registered_by, verified_at
        FROM signature.onsite_signer_bindings
        WHERE company_id = @CompanyId AND document_id = @DocumentId
        ORDER BY seq DESC
        LIMIT 1
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresOnSiteSignerBindingStore(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task RegisterAsync(OnSiteSignerBindingRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        using IDbConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            InsertSql,
            new
            {
                record.Id,
                record.CompanyId,
                record.DocumentId,
                record.SignerIdentity,
                record.VerificationMethod,
                RegisteredBy = record.RegisteredByUserId,
                VerifiedAt = record.VerifiedAtUtc,
            },
            cancellationToken: cancellationToken));
    }

    public async Task<OnSiteSignerBindingRecord?> ResolveVerifiedAsync(
        Guid companyId, Guid documentId, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            ResolveLatestSql,
            new { CompanyId = companyId, DocumentId = documentId },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        return new OnSiteSignerBindingRecord
        {
            Id = (Guid)row.id,
            CompanyId = (Guid)row.company_id,
            DocumentId = (Guid)row.document_id,
            SignerIdentity = (string)row.signer_identity,
            VerificationMethod = (string)row.verification_method,
            RegisteredByUserId = (Guid)row.registered_by,
            VerifiedAtUtc = OnSiteRowReader.ToDateTimeOffset((object)row.verified_at),
        };
    }
}
