namespace Liakont.Modules.Signature.Infrastructure.Persistence;

using Dapper;
using Liakont.Modules.Signature.Application;
using Liakont.Modules.Signature.Contracts;
using Liakont.Modules.Signature.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Store Dapper de la liaison référence fournisseur → document, tenant-scopé (ADR-0029 §5). Lit/écrit
/// <c>signature.signature_requests</c> dans la base DU tenant courant.
/// </summary>
internal sealed class PostgresSignatureRequestStore : ISignatureRequestStore
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresSignatureRequestStore(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task RecordAsync(SignatureRequestLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);

        const string sql = """
            INSERT INTO signature.signature_requests
                (company_id, provider_type, provider_reference, document_id, document_number, issue_date,
                 purpose, requested_level, created_at)
            VALUES
                (@CompanyId, @ProviderType, @ProviderReference, @DocumentId, @DocumentNumber, @IssueDate,
                 @Purpose, @RequestedLevel, now())
            ON CONFLICT (company_id, provider_type, provider_reference) DO UPDATE SET
                document_id = EXCLUDED.document_id,
                document_number = EXCLUDED.document_number,
                issue_date = EXCLUDED.issue_date,
                purpose = EXCLUDED.purpose,
                requested_level = EXCLUDED.requested_level
            """;

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                link.CompanyId,
                link.ProviderType,
                link.ProviderReference,
                link.DocumentId,
                link.DocumentNumber,
                link.IssueDate,
                link.Purpose,
                RequestedLevel = (int)link.RequestedLevel,
            },
            cancellationToken: cancellationToken));
    }

    public async Task<SignatureRequestLink?> GetByProviderReferenceAsync(
        Guid companyId, string providerType, string providerReference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerType);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerReference);

        const string sql = """
            SELECT company_id         AS CompanyId,
                   provider_type      AS ProviderType,
                   provider_reference AS ProviderReference,
                   document_id        AS DocumentId,
                   document_number    AS DocumentNumber,
                   issue_date         AS IssueDate,
                   purpose            AS Purpose,
                   requested_level    AS RequestedLevel,
                   created_at         AS CreatedAt
            FROM signature.signature_requests
            WHERE company_id = @CompanyId AND lower(provider_type) = lower(@ProviderType)
              AND provider_reference = @ProviderReference
            LIMIT 1
            """;

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<RequestRow>(
            new CommandDefinition(
                sql,
                new { CompanyId = companyId, ProviderType = providerType, ProviderReference = providerReference },
                cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        return new SignatureRequestLink
        {
            CompanyId = row.CompanyId,
            ProviderType = row.ProviderType,
            ProviderReference = row.ProviderReference,
            DocumentId = row.DocumentId,
            DocumentNumber = row.DocumentNumber,
            IssueDate = row.IssueDate,
            Purpose = row.Purpose,
            RequestedLevel = (SignatureLevel)row.RequestedLevel,
            CreatedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)),
        };
    }

    private sealed record RequestRow
    {
        public Guid CompanyId { get; init; }

        public string ProviderType { get; init; } = string.Empty;

        public string ProviderReference { get; init; } = string.Empty;

        public Guid DocumentId { get; init; }

        public string DocumentNumber { get; init; } = string.Empty;

        public DateOnly IssueDate { get; init; }

        public string? Purpose { get; init; }

        public int RequestedLevel { get; init; }

        public DateTime CreatedAt { get; init; }
    }
}
