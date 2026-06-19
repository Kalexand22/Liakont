namespace Liakont.Modules.Signature.Infrastructure.OnSite;

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Signature.Application.OnSite;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Journal de preuve de signature sur place sur Dapper/PostgreSQL (ADR-0030 §3 ; INV-ONSITE-6). APPEND-ONLY :
/// une seule opération d'écriture (INSERT) ; aucun update/delete (l'immuabilité est garantie au niveau base
/// par double trigger, CLAUDE.md n°4). Tenant-scopé par construction : la connexion EST la base du tenant
/// (database-per-tenant — CLAUDE.md n°9), donc aucune requête cross-tenant n'est possible.
/// </summary>
internal sealed class PostgresOnSiteSignatureProofStore : IOnSiteSignatureProofStore
{
    private const string InsertSql = """
        INSERT INTO signature.onsite_signature_proofs
            (id, company_id, document_id, binding_hash, uploader_user_id, signer_identity,
             signer_verified, level, proof_archive_ref, captured_at)
        VALUES
            (@Id, @CompanyId, @DocumentId, @BindingHash, @UploaderUserId, @SignerIdentity,
             @SignerVerified, @Level, @ProofArchiveRef, @CapturedAt)
        """;

    private const string FindLatestSql = """
        SELECT id, company_id, document_id, binding_hash, uploader_user_id, signer_identity,
               signer_verified, level, proof_archive_ref, captured_at
        FROM signature.onsite_signature_proofs
        WHERE company_id = @CompanyId AND document_id = @DocumentId
        ORDER BY seq DESC
        LIMIT 1
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresOnSiteSignatureProofStore(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task AppendAsync(OnSiteSignatureProofRecord record, CancellationToken cancellationToken = default)
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
                record.BindingHash,
                record.UploaderUserId,
                record.SignerIdentity,
                record.SignerVerified,
                record.Level,
                record.ProofArchiveRef,
                CapturedAt = record.CapturedAtUtc,
            },
            cancellationToken: cancellationToken));
    }

    public async Task<OnSiteSignatureProofRecord?> FindLatestAsync(
        Guid companyId, Guid documentId, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            FindLatestSql,
            new { CompanyId = companyId, DocumentId = documentId },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        return new OnSiteSignatureProofRecord
        {
            Id = (Guid)row.id,
            CompanyId = (Guid)row.company_id,
            DocumentId = (Guid)row.document_id,
            BindingHash = (string)row.binding_hash,
            UploaderUserId = (Guid)row.uploader_user_id,
            SignerIdentity = row.signer_identity as string,
            SignerVerified = (bool)row.signer_verified,
            Level = (string)row.level,
            ProofArchiveRef = row.proof_archive_ref as string,
            CapturedAtUtc = OnSiteRowReader.ToDateTimeOffset((object)row.captured_at),
        };
    }
}
