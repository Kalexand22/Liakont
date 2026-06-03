namespace Stratum.Common.Infrastructure.Tests.Integration.CrossTenant;

using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.CrossTenant;
using Stratum.Common.Infrastructure.CrossTenant;
using Stratum.Common.Infrastructure.CrossTenant.TestPing;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Tests.Integration.Portal;
using Xunit;

/// <summary>
/// Fixture that provisions a system DB + 2 tenant DBs (with inbound_pings table),
/// and provides helpers for publishing, dispatching, and querying cross-tenant events.
/// </summary>
public sealed class CrossTenantEndToEndFixture : IAsyncLifetime
{
    private readonly MultiTenantFixture _multiTenant = new();

    private NpgsqlConnectionFactory? _systemConnectionFactory;

    public async Task InitializeAsync()
    {
        await _multiTenant.InitializeAsync();

        // Create the crossdata.inbound_pings table in both tenant DBs
        await CreateInboundPingsTableAsync(MultiTenantFixture.TenantA);
        await CreateInboundPingsTableAsync(MultiTenantFixture.TenantB);

        _systemConnectionFactory = new NpgsqlConnectionFactory(
            Options.Create(new DatabaseOptions { ConnectionString = _multiTenant.SystemConnectionString }));
    }

    public async Task DisposeAsync()
    {
        await _multiTenant.DisposeAsync();
    }

    public CrossTenantPublisher CreatePublisher()
    {
        return new CrossTenantPublisher(
            _systemConnectionFactory!,
            NullLogger<CrossTenantPublisher>.Instance);
    }

    /// <summary>
    /// Runs the dispatcher until all pending events are delivered (or timeout).
    /// Uses the public DI API (<see cref="ServiceCollectionExtensions.AddCrossTenantHandlers"/>)
    /// to build the handler registry, proving the production registration path.
    /// </summary>
    public async Task RunDispatchCycleAsync()
    {
        // Build handler registry through the public DI API
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantConnectionFactory>(_multiTenant.CreateTenantConnectionFactory());
        services.AddCrossTenantHandlers(typeof(InboundPingHandler).Assembly);
        await using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<ICrossTenantHandlerRegistry>();

        var options = Options.Create(new CrossTenantDispatcherOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(50),
            BatchSize = 100,
            MaxRetries = 5,
        });

        var dispatcher = new CrossTenantDispatcher(
            _systemConnectionFactory!,
            registry,
            options,
            NullLogger<CrossTenantDispatcher>.Instance);

        using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await dispatcher.StartAsync(startCts.Token);

        // Poll until no pending events remain (deterministic, not timing-dependent)
        await WaitForAllDeliveredAsync(TimeSpan.FromSeconds(5));

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await dispatcher.StopAsync(stopCts.Token);
    }

    private async Task WaitForAllDeliveredAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var conn = await _systemConnectionFactory!.OpenAsync();
            var pendingCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM outbox.cross_tenant_events WHERE status IN ('pending', 'failed') AND event_type LIKE 'Test.%'");

            if (pendingCount == 0)
            {
                return;
            }

            await Task.Delay(50, CancellationToken.None);
        }
    }

    public async Task<InboundPingRow?> GetLatestInboundPingAsync(string tenantId)
    {
        await using var conn = await OpenTenantConnectionAsync(tenantId);
        return await conn.QuerySingleOrDefaultAsync<InboundPingRow>(
            """
            SELECT event_id     AS "EventId",
                   source_tenant AS "SourceTenant",
                   message       AS "Message",
                   submitter_email AS "SubmitterEmail",
                   received_at  AS "ReceivedAt"
            FROM crossdata.inbound_pings
            ORDER BY received_at DESC
            LIMIT 1
            """);
    }

    public async Task<int> CountInboundPingsAsync(string tenantId)
    {
        await using var conn = await OpenTenantConnectionAsync(tenantId);
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM crossdata.inbound_pings");
    }

    public async Task<string?> GetEventStatusAsync(string eventType, string targetTenant)
    {
        using var conn = await _systemConnectionFactory!.OpenAsync();
        return await conn.ExecuteScalarAsync<string>(
            """
            SELECT status FROM outbox.cross_tenant_events
            WHERE event_type = @EventType AND target_tenant = @TargetTenant
            ORDER BY created_at DESC LIMIT 1
            """,
            new { EventType = eventType, TargetTenant = targetTenant });
    }

    public async Task ResetEventStatusToPendingAsync(string eventType, string targetTenant)
    {
        using var conn = await _systemConnectionFactory!.OpenAsync();
        await conn.ExecuteAsync(
            """
            UPDATE outbox.cross_tenant_events
            SET status = 'pending', delivered_at = NULL
            WHERE event_type = @EventType AND target_tenant = @TargetTenant
            """,
            new { EventType = eventType, TargetTenant = targetTenant });
    }

    public async Task CleanInboundPingsAsync(string tenantId)
    {
        await using var conn = await OpenTenantConnectionAsync(tenantId);
        await conn.ExecuteAsync("TRUNCATE crossdata.inbound_pings");
    }

    public async Task CleanOutboxEventsAsync()
    {
        using var conn = await _systemConnectionFactory!.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM outbox.cross_tenant_events WHERE event_type LIKE 'Test.%'");
    }

    private async Task<NpgsqlConnection> OpenTenantConnectionAsync(string tenantId)
    {
        var connStr = BuildTenantConnectionString(tenantId);
        var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        return conn;
    }

    private string BuildTenantConnectionString(string tenantId)
    {
        var builder = new NpgsqlConnectionStringBuilder(_multiTenant.SystemConnectionString)
        {
            Database = $"stratum_{tenantId.Replace('-', '_')}",
        };
        return builder.ToString();
    }

    private async Task CreateInboundPingsTableAsync(string tenantId)
    {
        await using var conn = await OpenTenantConnectionAsync(tenantId);
        await conn.ExecuteAsync(
            """
            CREATE SCHEMA IF NOT EXISTS crossdata;

            CREATE TABLE IF NOT EXISTS crossdata.inbound_pings (
                id              uuid        NOT NULL DEFAULT gen_random_uuid(),
                event_id        uuid        NOT NULL,
                source_tenant   text,
                message         text        NOT NULL,
                submitter_email text,
                received_at     timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT pk_inbound_pings PRIMARY KEY (id),
                CONSTRAINT uq_inbound_pings_event_id UNIQUE (event_id)
            );
            """);
    }

    public sealed record InboundPingRow
    {
        public Guid EventId { get; init; }

        public string? SourceTenant { get; init; }

        public string Message { get; init; } = string.Empty;

        public string? SubmitterEmail { get; init; }

        public DateTimeOffset ReceivedAt { get; init; }
    }
}
