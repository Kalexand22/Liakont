namespace Liakont.Host.Tests.Unit.Supervision;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Supervision;
using Liakont.Modules.Supervision.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Tests d'intégration légère de <see cref="SupervisionLivenessProvider.GetAsync"/> (FIX210, F12 §5.1) :
/// résolution du Contract Supervision dans un scope DI neuf (système), puis verdict (sain / en retard / jamais
/// évaluée) et repli best-effort sur « état indéterminé » si la lecture échoue. Utilise un vrai
/// <see cref="IServiceScopeFactory"/> (ServiceCollection) pour traverser le chemin DI exact.
/// </summary>
public sealed class SupervisionLivenessProviderGetAsyncTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_Returns_Healthy_When_Last_Evaluation_Is_Recent()
    {
        var lastEval = Now.AddMinutes(-10);
        var provider = Build(new FakeLivenessQueries(lastEval));

        var view = await provider.GetAsync();

        view.Status.Should().Be(SupervisionLivenessStatus.Healthy);
        view.LastEvaluationUtc.Should().Be(lastEval);
    }

    [Fact]
    public async Task GetAsync_Returns_Overdue_When_Last_Evaluation_Is_3h_Old()
    {
        var provider = Build(new FakeLivenessQueries(Now.AddHours(-3)));

        var view = await provider.GetAsync();

        view.Status.Should().Be(SupervisionLivenessStatus.Overdue);
    }

    [Fact]
    public async Task GetAsync_Returns_NeverEvaluated_When_No_Evaluation_Recorded()
    {
        var provider = Build(new FakeLivenessQueries(null));

        var view = await provider.GetAsync();

        view.Status.Should().Be(SupervisionLivenessStatus.NeverEvaluated);
    }

    [Fact]
    public async Task GetAsync_Returns_Unknown_When_The_Read_Throws()
    {
        var provider = Build(new ThrowingLivenessQueries());

        var view = await provider.GetAsync();

        view.Status.Should().Be(SupervisionLivenessStatus.Unknown);
    }

    private static SupervisionLivenessProvider Build(ISupervisionLivenessQueries livenessQueries)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => livenessQueries);

        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        return new SupervisionLivenessProvider(
            scopeFactory,
            new FixedTimeProvider(Now),
            NullLogger<SupervisionLivenessProvider>.Instance);
    }

    private sealed class FakeLivenessQueries : ISupervisionLivenessQueries
    {
        private readonly DateTimeOffset? _lastEvaluationUtc;

        public FakeLivenessQueries(DateTimeOffset? lastEvaluationUtc) => _lastEvaluationUtc = lastEvaluationUtc;

        public Task<DateTimeOffset?> GetLastEvaluationUtcAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_lastEvaluationUtc);
    }

    private sealed class ThrowingLivenessQueries : ISupervisionLivenessQueries
    {
        public Task<DateTimeOffset?> GetLastEvaluationUtcAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("base indisponible");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
