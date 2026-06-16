namespace Liakont.Modules.Signature.Infrastructure.Persistence;

using Dapper;
using Liakont.Modules.Signature.Application;
using Liakont.Modules.Signature.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Inbox Dapper durable des webhooks de signature, tenant-scopée (ADR-0029 §4 ; INV-YOUSIGN-4/5). Persiste
/// dans <c>signature.signature_webhook_inbox</c> (base DU tenant courant). L'idempotence est garantie par la
/// contrainte UNIQUE <c>(company_id, provider_type, event_id)</c> + <c>ON CONFLICT DO NOTHING</c>.
/// </summary>
internal sealed class PostgresSignatureWebhookInbox : ISignatureWebhookInbox
{
    /// <summary>
    /// Nombre maximal de tentatives de drain avant qu'une entrée soit exclue du lot (mise de côté). Les entrées
    /// dépassant ce seuil restent dans la table avec leur <c>last_error</c> pour inspection opérateur ; elles ne
    /// bloquent plus le traitement des entrées suivantes.
    /// </summary>
    public const int MaxDrainAttempts = 10;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresSignatureWebhookInbox(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> EnqueueAsync(SignatureWebhookInboxItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        const string sql = """
            INSERT INTO signature.signature_webhook_inbox
                (id, company_id, provider_type, event_id, provider_reference, raw_body, received_at, attempt_count)
            VALUES
                (@Id, @CompanyId, @ProviderType, @EventId, @ProviderReference, @RawBody, @ReceivedAt, 0)
            ON CONFLICT (company_id, provider_type, event_id) DO NOTHING
            """;

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var inserted = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                item.Id,
                item.CompanyId,
                item.ProviderType,
                item.EventId,
                item.ProviderReference,
                item.RawBody,
                ReceivedAt = item.ReceivedAtUtc == default ? DateTimeOffset.UtcNow : item.ReceivedAtUtc,
            },
            cancellationToken: cancellationToken));

        // 1 = inséré (nouvel événement) ; 0 = conflit (rejeu — idempotence à l'inbox).
        return inserted == 1;
    }

    public async Task<IReadOnlyList<SignatureWebhookInboxItem>> DrainPendingAsync(
        int max, CancellationToken cancellationToken = default)
    {
        if (max <= 0)
        {
            return [];
        }

        const string sql = """
            SELECT id                 AS Id,
                   company_id         AS CompanyId,
                   provider_type      AS ProviderType,
                   event_id           AS EventId,
                   provider_reference AS ProviderReference,
                   raw_body           AS RawBody,
                   received_at        AS ReceivedAt,
                   processed_at       AS ProcessedAt,
                   attempt_count      AS AttemptCount,
                   last_error         AS LastError
            FROM signature.signature_webhook_inbox
            WHERE processed_at IS NULL AND attempt_count < @MaxAttempts
            ORDER BY received_at ASC
            LIMIT @Max
            """;

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<InboxRow>(
            new CommandDefinition(sql, new { Max = max, MaxAttempts = MaxDrainAttempts }, cancellationToken: cancellationToken));

        // Conversion robuste des horodatages (timestamptz → DateTimeOffset, indépendante de la représentation
        // CLR exacte renvoyée par Npgsql — même posture que DocumentApprovalRowReader).
        return rows.Select(r => new SignatureWebhookInboxItem
        {
            Id = r.Id,
            CompanyId = r.CompanyId,
            ProviderType = r.ProviderType,
            EventId = r.EventId,
            ProviderReference = r.ProviderReference,
            RawBody = r.RawBody,
            ReceivedAtUtc = ToDateTimeOffset(r.ReceivedAt),
            ProcessedAtUtc = r.ProcessedAt is null ? null : ToDateTimeOffset(r.ProcessedAt.Value),
            AttemptCount = r.AttemptCount,
            LastError = r.LastError,
        }).ToList();
    }

    public async Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE signature.signature_webhook_inbox
            SET processed_at = now()
            WHERE id = @Id AND processed_at IS NULL
            """;

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task MarkFailedAsync(Guid id, string errorMessage, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE signature.signature_webhook_inbox
            SET attempt_count = attempt_count + 1, last_error = @Error
            WHERE id = @Id AND processed_at IS NULL
            """;

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { Id = id, Error = Truncate(errorMessage, 2000) }, cancellationToken: cancellationToken));
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value ?? string.Empty : value[..max];

    private sealed record InboxRow
    {
        public Guid Id { get; init; }

        public Guid CompanyId { get; init; }

        public string ProviderType { get; init; } = string.Empty;

        public string EventId { get; init; } = string.Empty;

        public string ProviderReference { get; init; } = string.Empty;

        public byte[] RawBody { get; init; } = [];

        public DateTime ReceivedAt { get; init; }

        public DateTime? ProcessedAt { get; init; }

        public int AttemptCount { get; init; }

        public string? LastError { get; init; }
    }
}
