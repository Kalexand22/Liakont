namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Supervision;

/// <summary>
/// Faux <see cref="ISupervisionLivenessProvider"/> pour les tests bUnit des pages de supervision : rend un
/// témoin de vie « sain » par défaut, sans toucher au module Job. Les pages embarquent le bandeau
/// <c>SupervisionLivenessBanner</c> (FIX210) qui résout ce service à l'initialisation.
/// </summary>
internal sealed class FakeSupervisionLivenessProvider : ISupervisionLivenessProvider
{
    private readonly SupervisionLivenessView _view;

    public FakeSupervisionLivenessProvider(SupervisionLivenessStatus status = SupervisionLivenessStatus.Healthy)
    {
        _view = new SupervisionLivenessView
        {
            Status = status,
            LastEvaluationUtc = new DateTimeOffset(2026, 6, 11, 11, 55, 0, TimeSpan.Zero),
            IntervalMinutes = 15,
        };
    }

    public Task<SupervisionLivenessView> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_view);
}
