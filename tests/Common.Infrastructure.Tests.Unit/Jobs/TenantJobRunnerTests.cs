namespace Stratum.Common.Infrastructure.Tests.Unit.Jobs;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Jobs;
using Xunit;

public sealed class TenantJobRunnerTests
{
    [Fact]
    public async Task RunForAllTenantsAsync_Should_RunOnlyActiveTenants()
    {
        var queries = new FakeTenantQueries(
            Tenant("tenant-a", isActive: true),
            Tenant("tenant-inactive", isActive: false),
            Tenant("tenant-b", isActive: true));
        var scopeFactory = new RecordingTenantScopeFactory();
        var job = new RecordingTenantJob();
        var runner = new TenantJobRunner(queries, scopeFactory, NullLogger<TenantJobRunner>.Instance);

        var summary = await runner.RunForAllTenantsAsync(job);

        job.ExecutedTenantIds.Should().Equal("tenant-a", "tenant-b");
        summary.TotalTenants.Should().Be(2);
        summary.SucceededCount.Should().Be(2);
        summary.FailedCount.Should().Be(0);
        summary.JobName.Should().Be(job.Name);
    }

    [Fact]
    public async Task RunForAllTenantsAsync_Should_IsolateFailures_When_OneTenantThrows()
    {
        var queries = new FakeTenantQueries(
            Tenant("tenant-a", isActive: true),
            Tenant("tenant-b", isActive: true),
            Tenant("tenant-c", isActive: true));
        var scopeFactory = new RecordingTenantScopeFactory();
        var job = new RecordingTenantJob(throwFor: id => id == "tenant-b");
        var runner = new TenantJobRunner(queries, scopeFactory, NullLogger<TenantJobRunner>.Instance);

        var summary = await runner.RunForAllTenantsAsync(job);

        // Every active tenant is attempted, even after one throws.
        job.ExecutedTenantIds.Should().Equal("tenant-a", "tenant-b", "tenant-c");
        summary.SucceededCount.Should().Be(2);
        summary.FailedCount.Should().Be(1);
        summary.Failures.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { TenantId = "tenant-b" });
        summary.Failures[0].ErrorMessage.Should().Contain("tenant-b");
    }

    [Fact]
    public async Task RunForAllTenantsAsync_Should_ReturnEmptySummary_When_NoActiveTenants()
    {
        var queries = new FakeTenantQueries(
            Tenant("tenant-inactive-1", isActive: false),
            Tenant("tenant-inactive-2", isActive: false));
        var scopeFactory = new RecordingTenantScopeFactory();
        var job = new RecordingTenantJob();
        var runner = new TenantJobRunner(queries, scopeFactory, NullLogger<TenantJobRunner>.Instance);

        var summary = await runner.RunForAllTenantsAsync(job);

        job.ExecutedTenantIds.Should().BeEmpty();
        scopeFactory.CreatedScopes.Should().BeEmpty();
        summary.TotalTenants.Should().Be(0);
        summary.SucceededCount.Should().Be(0);
        summary.FailedCount.Should().Be(0);
    }

    [Fact]
    public async Task RunForAllTenantsAsync_Should_DisposeScope_PerTenant_IncludingOnFailure()
    {
        var queries = new FakeTenantQueries(
            Tenant("tenant-a", isActive: true),
            Tenant("tenant-b", isActive: true));
        var scopeFactory = new RecordingTenantScopeFactory();
        var job = new RecordingTenantJob(throwFor: id => id == "tenant-b");
        var runner = new TenantJobRunner(queries, scopeFactory, NullLogger<TenantJobRunner>.Instance);

        await runner.RunForAllTenantsAsync(job);

        scopeFactory.CreatedScopes.Should().HaveCount(2);
        scopeFactory.CreatedScopes.Should().OnlyContain(s => s.Disposed);
    }

    [Fact]
    public async Task RunForAllTenantsAsync_Should_HandJob_TheTenantScopedServices()
    {
        var queries = new FakeTenantQueries(Tenant("tenant-a", isActive: true));
        var scopeFactory = new RecordingTenantScopeFactory();
        var job = new RecordingTenantJob();
        var runner = new TenantJobRunner(queries, scopeFactory, NullLogger<TenantJobRunner>.Instance);

        await runner.RunForAllTenantsAsync(job);

        job.ReceivedServices.Should().ContainSingle()
            .Which.Should().BeSameAs(scopeFactory.CreatedScopes[0].Services);
    }

    [Fact]
    public async Task RunForAllTenantsAsync_Should_Propagate_When_Cancelled()
    {
        var queries = new FakeTenantQueries(Tenant("tenant-a", isActive: true));
        var scopeFactory = new RecordingTenantScopeFactory();
        var job = new RecordingTenantJob();
        var runner = new TenantJobRunner(queries, scopeFactory, NullLogger<TenantJobRunner>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await runner.RunForAllTenantsAsync(job, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        job.ExecutedTenantIds.Should().BeEmpty();
    }

    [Fact]
    public async Task RunForAllTenantsAsync_Should_Throw_When_JobIsNull()
    {
        var runner = new TenantJobRunner(
            new FakeTenantQueries(),
            new RecordingTenantScopeFactory(),
            NullLogger<TenantJobRunner>.Instance);

        var act = async () => await runner.RunForAllTenantsAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunForAllTenantsAsync_Should_Abort_When_JobThrowsOce_ForCallerToken()
    {
        var queries = new FakeTenantQueries(
            Tenant("tenant-1", isActive: true),
            Tenant("tenant-2", isActive: true));
        var scopeFactory = new RecordingTenantScopeFactory();
        using var cts = new CancellationTokenSource();
        var job = new DelegatingTenantJob(_ =>
        {
            // Caller cancellation observed mid-execution: the run must abort, not isolate this as a failure.
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var runner = new TenantJobRunner(queries, scopeFactory, NullLogger<TenantJobRunner>.Instance);

        var act = async () => await runner.RunForAllTenantsAsync(job, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        job.ExecutedTenantIds.Should().Equal("tenant-1"); // tenant-2 never attempted (run aborted)
    }

    [Fact]
    public async Task RunForAllTenantsAsync_Should_ReportFailure_When_JobThrowsOce_WithoutCallerCancellation()
    {
        var queries = new FakeTenantQueries(
            Tenant("tenant-1", isActive: true),
            Tenant("tenant-2", isActive: true));
        var scopeFactory = new RecordingTenantScopeFactory();
        var job = new DelegatingTenantJob(context =>
        {
            if (context.TenantId == "tenant-1")
            {
                // OCE unrelated to the caller token → isolated as a per-tenant failure; run continues.
                throw new OperationCanceledException("unrelated to caller token");
            }

            return Task.CompletedTask;
        });
        var runner = new TenantJobRunner(queries, scopeFactory, NullLogger<TenantJobRunner>.Instance);

        var summary = await runner.RunForAllTenantsAsync(job);

        job.ExecutedTenantIds.Should().Equal("tenant-1", "tenant-2");
        summary.SucceededCount.Should().Be(1);
        summary.FailedCount.Should().Be(1);
        summary.Failures.Should().ContainSingle().Which.TenantId.Should().Be("tenant-1");
    }

    private static TenantDto Tenant(string id, bool isActive) => new()
    {
        Id = id,
        DisplayName = id,
        AdminEmail = $"admin@{id}.test",
        DatabaseName = $"stratum_{id}",
        IsActive = isActive,
        ProvisionedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class FakeTenantQueries : ITenantQueries
    {
        private readonly IReadOnlyList<TenantDto> _tenants;

        public FakeTenantQueries(params TenantDto[] tenants) => _tenants = tenants;

        public Task<IReadOnlyList<TenantDto>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_tenants);

        public Task<TenantDto?> GetByIdAsync(string tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(_tenants.FirstOrDefault(t => t.Id == tenantId));
    }

    private sealed class RecordingTenantScopeFactory : ITenantScopeFactory
    {
        public List<RecordingTenantScope> CreatedScopes { get; } = [];

        public ITenantScope Create(string tenantId)
        {
            var scope = new RecordingTenantScope(tenantId);
            CreatedScopes.Add(scope);
            return scope;
        }
    }

    private sealed class RecordingTenantScope : ITenantScope
    {
        public RecordingTenantScope(string tenantId) => TenantId = tenantId;

        public string TenantId { get; }

        public IServiceProvider Services { get; } = new EmptyServiceProvider();

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class RecordingTenantJob : ITenantJob
    {
        private readonly Func<string, bool>? _throwFor;

        public RecordingTenantJob(string name = "test.job", Func<string, bool>? throwFor = null)
        {
            Name = name;
            _throwFor = throwFor;
        }

        public string Name { get; }

        public List<string> ExecutedTenantIds { get; } = [];

        public List<IServiceProvider> ReceivedServices { get; } = [];

        public Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
        {
            ExecutedTenantIds.Add(context.TenantId);
            ReceivedServices.Add(context.Services);

            if (_throwFor is not null && _throwFor(context.TenantId))
            {
                throw new InvalidOperationException($"boom for {context.TenantId}");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class DelegatingTenantJob : ITenantJob
    {
        private readonly Func<TenantJobContext, Task> _body;

        public DelegatingTenantJob(Func<TenantJobContext, Task> body, string name = "test.delegating")
        {
            _body = body;
            Name = name;
        }

        public string Name { get; }

        public List<string> ExecutedTenantIds { get; } = [];

        public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
        {
            ExecutedTenantIds.Add(context.TenantId);
            await _body(context);
        }
    }
}
