namespace Liakont.Host.Tests.Unit.Supervision;

using System;
using FluentAssertions;
using Liakont.Host.Supervision;
using Xunit;

/// <summary>
/// Décision PURE du témoin de vie (FIX210, F12 §5.1) : jamais évaluée, en retard (au-delà du double de la
/// cadence) ou saine. Verrouille les frontières du « retard » pour éviter une fausse alerte sur un système sain.
/// </summary>
public sealed class SupervisionLivenessProviderTests
{
    private const int Interval = 15;

    private static readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_Evaluation_Yields_NeverEvaluated()
    {
        var view = SupervisionLivenessProvider.Evaluate(null, Now, Interval);

        view.Status.Should().Be(SupervisionLivenessStatus.NeverEvaluated);
        view.LastEvaluationUtc.Should().BeNull();
        view.IntervalMinutes.Should().Be(Interval);
    }

    [Fact]
    public void Recent_Evaluation_Is_Healthy()
    {
        var view = SupervisionLivenessProvider.Evaluate(Now.AddMinutes(-10), Now, Interval);

        view.Status.Should().Be(SupervisionLivenessStatus.Healthy);
        view.LastEvaluationUtc.Should().Be(Now.AddMinutes(-10));
    }

    [Fact]
    public void Exactly_Double_The_Interval_Is_Still_Healthy()
    {
        // Tolérance « au-delà du double de la cadence » stricte : pile à 30 min n'est pas encore en retard.
        var view = SupervisionLivenessProvider.Evaluate(Now.AddMinutes(-30), Now, Interval);

        view.Status.Should().Be(SupervisionLivenessStatus.Healthy);
    }

    [Fact]
    public void Beyond_Double_The_Interval_Is_Overdue()
    {
        var view = SupervisionLivenessProvider.Evaluate(Now.AddMinutes(-31), Now, Interval);

        view.Status.Should().Be(SupervisionLivenessStatus.Overdue);
    }

    [Fact]
    public void Long_Silence_Is_Overdue()
    {
        var view = SupervisionLivenessProvider.Evaluate(Now.AddHours(-3), Now, Interval);

        view.Status.Should().Be(SupervisionLivenessStatus.Overdue);
    }
}
