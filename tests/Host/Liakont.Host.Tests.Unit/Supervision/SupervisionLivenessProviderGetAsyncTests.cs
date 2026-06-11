namespace Liakont.Host.Tests.Unit.Supervision;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Supervision;
using Liakont.Modules.Supervision.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Modules.Job.Contracts.DTOs;
using Stratum.Modules.Job.Contracts.Queries;
using Xunit;

/// <summary>
/// Tests d'intégration légère de <see cref="SupervisionLivenessProvider.GetAsync"/> (FIX210, F12 §5.1) :
/// filtre par type de job, prise du Max(CompletedAt), et repli best-effort sur « état indéterminé ».
/// Utilise un vrai <see cref="IServiceScopeFactory"/> (ServiceCollection) pour traverser le chemin DI exact.
/// </summary>
public sealed class SupervisionLivenessProviderGetAsyncTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly string SupervisionJobType = typeof(SupervisionEvaluationTrigger).FullName!;

    private static JobDto MakeJob(string type, DateTimeOffset? completedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            Status = "Completed",
            Priority = 0,
            MaxRetries = 3,
            RetryCount = 0,
            ScheduledAt = Now.AddMinutes(-20),
            CreatedAt = Now.AddMinutes(-20),
            CompletedAt = completedAt,
        };

    private static SupervisionLivenessProvider Build(IJobQueries jobQueries)
    {
        var services = new ServiceCollection();
        services.AddScoped<IJobQueries>(_ => jobQueries);

        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        return new SupervisionLivenessProvider(
            scopeFactory,
            new FixedTimeProvider(Now),
            NullLogger<SupervisionLivenessProvider>.Instance);
    }

    [Fact]
    public async Task GetAsync_Returns_Healthy_When_Recent_Completed_Supervision_Job_Exists()
    {
        var completedAt = Now.AddMinutes(-10);
        var fake = new FakeJobQueries([MakeJob(SupervisionJobType, completedAt)]);
        var provider = Build(fake);

        var view = await provider.GetAsync();

        view.Status.Should().Be(SupervisionLivenessStatus.Healthy);
        view.LastEvaluationUtc.Should().Be(completedAt);
    }

    [Fact]
    public async Task GetAsync_Returns_Overdue_When_Last_Supervision_Job_Is_3h_Old()
    {
        var completedAt = Now.AddHours(-3);
        var fake = new FakeJobQueries([MakeJob(SupervisionJobType, completedAt)]);
        var provider = Build(fake);

        var view = await provider.GetAsync();

        view.Status.Should().Be(SupervisionLivenessStatus.Overdue);
    }

    [Fact]
    public async Task GetAsync_Returns_NeverEvaluated_When_No_Job_Matches_Supervision_Type()
    {
        var fake = new FakeJobQueries([MakeJob("Other.Job.Type", Now.AddMinutes(-5))]);
        var provider = Build(fake);

        var view = await provider.GetAsync();

        view.Status.Should().Be(SupervisionLivenessStatus.NeverEvaluated);
    }

    [Fact]
    public async Task GetAsync_Returns_Unknown_When_JobQueries_Throws()
    {
        var fake = new ThrowingJobQueries();
        var provider = Build(fake);

        var view = await provider.GetAsync();

        view.Status.Should().Be(SupervisionLivenessStatus.Unknown);
    }

    private sealed class FakeJobQueries : IJobQueries
    {
        private readonly IReadOnlyList<JobDto> _jobs;

        public FakeJobQueries(IReadOnlyList<JobDto> jobs) => _jobs = jobs;

        public Task<JobDto?> GetByIdAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(_jobs.FirstOrDefault(j => j.Id == jobId));

        public Task<IReadOnlyList<JobDto>> ListByStatusAsync(string status, int limit = 50, CancellationToken ct = default) =>
            Task.FromResult(_jobs);
    }

    private sealed class ThrowingJobQueries : IJobQueries
    {
        public Task<JobDto?> GetByIdAsync(Guid jobId, CancellationToken ct = default) =>
            throw new InvalidOperationException("base indisponible");

        public Task<IReadOnlyList<JobDto>> ListByStatusAsync(string status, int limit = 50, CancellationToken ct = default) =>
            throw new InvalidOperationException("base indisponible");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
