namespace Liakont.Host.Tests.Unit.Supervision;

using System.Linq;
using FluentAssertions;
using Liakont.Host.Startup;
using Liakont.Modules.Supervision.Contracts;
using Liakont.Modules.Supervision.Infrastructure;
using Xunit;

/// <summary>
/// Verrouille la cohérence cadence ↔ cron (FIX210, F12 §5.1) : si l'expression cron de
/// <see cref="SystemJobDefinitions"/> change sans mettre à jour
/// <see cref="SupervisionEvaluationCadence.IntervalMinutes"/>, la fenêtre « en retard » (×2)
/// devient silencieusement fausse. Les deux DOIVENT changer ensemble — ce test casse sinon.
/// </summary>
public sealed class SupervisionCadenceLockTests
{
    [Fact]
    public void Supervision_CronExpression_Must_Match_EvaluationIntervalMinutes()
    {
        // Localise l'entrée de supervision dans la liste UNIQUE des jobs système.
        var entry = SystemJobDefinitions.All
            .SingleOrDefault(d => d.JobType == typeof(SupervisionEvaluationTrigger).FullName);

        entry.Should().NotBeNull("le job de supervision doit être déclaré dans SystemJobDefinitions.All");

        var expectedCron = $"*/{SupervisionEvaluationCadence.IntervalMinutes} * * * *";

        var reason = $"la cadence F12 §5.1 est {SupervisionEvaluationCadence.IntervalMinutes} min — "
            + "SupervisionEvaluationCadence.IntervalMinutes et le cron de SystemJobDefinitions doivent changer ensemble";
        entry!.CronExpression.Should().Be(expectedCron, reason);
    }
}
