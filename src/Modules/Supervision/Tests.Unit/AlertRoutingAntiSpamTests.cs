namespace Liakont.Modules.Supervision.Tests.Unit;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.Supervision.Infrastructure;
using Liakont.Modules.Supervision.Tests.Unit.Doubles;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Le routage des alertes (FIX212) ne change QUE le choix des destinataires : l'anti-spam reste porté par le
/// moteur (notification aux seules transitions). Vérifié de bout en bout : moteur réel + notifieur réel +
/// matrice configurée — une alerte qui reste active ne ré-enfile aucun e-mail à la seconde évaluation.
/// </summary>
public sealed class AlertRoutingAntiSpamTests
{
    private const string Tenant = "acme";
    private const string OperatorEmail = "operateur@liakont.test";
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Company = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Routing_Preserves_AntiSpam_Across_ReEvaluations()
    {
        var store = new InMemoryAlertStore();
        var clock = new FixedTimeProvider(Now);
        var queue = new RecordingJobQueue();
        var tenantSettings = new FakeTenantSettingsQueries(Company);
        var routing = new FakeAlertRoutingQueries(new AlertRoutingRuleDto
        {
            RuleKey = null,
            Severity = "Critical",
            Recipients = ["compta@acme.test"],
            Ordinal = 0,
        });
        var notifier = new AlertEmailNotifier(
            queue,
            tenantSettings,
            routing,
            new FakeAlertQueries(),
            Options.Create(new SupervisionNotificationOptions { OperatorEmail = OperatorEmail }),
            NullLogger<AlertEmailNotifier>.Instance);

        var rule = new FakeAlertRule("agent.mute", AlertSeverity.Critical, isFiring: true);
        var engine = new AlertEvaluationService([rule], store, clock, notifier);

        await engine.EvaluateAsync(Tenant);
        var afterFirstEvaluation = queue.Emails.Count;

        clock.Advance(TimeSpan.FromMinutes(15));
        await engine.EvaluateAsync(Tenant);

        // Déclenchement : opérateur (toutes) + destinataire de la matrice (critiques) = 2 e-mails.
        afterFirstEvaluation.Should().Be(2);

        // Alerte toujours active à la 2e évaluation : aucune nouvelle mise en file (anti-spam conservé).
        queue.Emails.Should().HaveCount(afterFirstEvaluation);
    }
}
