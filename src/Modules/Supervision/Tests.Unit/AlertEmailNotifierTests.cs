namespace Liakont.Modules.Supervision.Tests.Unit;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Contracts;
using Liakont.Modules.Supervision.Contracts.DTOs;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.Supervision.Infrastructure;
using Liakont.Modules.Supervision.Tests.Unit.Doubles;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class AlertEmailNotifierTests
{
    private const string Tenant = "acme";
    private const string OperatorEmail = "operateur@liakont.test";
    private const string TenantContact = "compta@acme.test";
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 9, 30, 0, TimeSpan.Zero);
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static AlertEmailNotifier BuildNotifier(
        RecordingJobQueue queue,
        ITenantSettingsQueries tenantSettings,
        SupervisionNotificationOptions options,
        IAlertQueries? alertQueries = null) =>
        new(
            queue,
            tenantSettings,
            alertQueries ?? new FakeAlertQueries(),
            Options.Create(options),
            NullLogger<AlertEmailNotifier>.Instance);

    private static Alert RaiseAlert(AlertSeverity severity, string? detail = "Détail actionnable.") =>
        Alert.Raise(Tenant, "agent.mute", severity, detail, Now);

    private static AlertThresholdsDto Thresholds(bool tenantContact) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Company,
        AgentSilentHours = 24,
        MissedRunHours = 36,
        PushQueueMaxItems = 50,
        PushQueueMaxAgeHours = 6,
        BlockedDocumentsDays = 5,
        PaRejectionsDays = 2,
        AlertTenantContact = tenantContact,
        CreatedAt = Now,
    };

    private static TenantProfileDto Profile(string? contactEmail) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Company,
        Siren = "123456789",
        RaisonSociale = "ACME",
        Street = "1 rue de la Paix",
        PostalCode = "75002",
        City = "Paris",
        Country = "FR",
        ContactEmailAlerte = contactEmail,
        Statut = "Actif",
        CreatedAt = Now,
    };

    private static AlertDto ActiveDto(AlertSeverity severity, string ruleKey = "agent.mute") => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Tenant,
        RuleKey = ruleKey,
        Severity = severity.ToString(),
        Detail = "Détail.",
        TriggeredUtc = Now,
        ResolvedUtc = null,
    };

    [Fact]
    public async Task Raise_Sends_To_Operator_With_Actionable_French_Body()
    {
        var queue = new RecordingJobQueue();
        var tenantSettings = new FakeTenantSettingsQueries(Company);
        var notifier = BuildNotifier(queue, tenantSettings, new SupervisionNotificationOptions { OperatorEmail = OperatorEmail });

        await notifier.NotifyRaisedAsync(RaiseAlert(AlertSeverity.Warning, "L'agent du client acme ne répond plus."));

        queue.Emails.Should().ContainSingle();
        var email = queue.Emails[0];
        email.RecipientEmail.Should().Be(OperatorEmail);
        email.LanguageCode.Should().Be("fr");
        email.Subject.Should().Contain("avertissement").And.Contain(Tenant);
        email.Body.Should().Contain(Tenant).And.Contain("agent.mute").And.Contain("L'agent du client acme ne répond plus.");
        queue.Enqueued[0].CompanyId.Should().Be(Company);
    }

    [Fact]
    public async Task Raise_Critical_Also_Emails_Tenant_Contact_When_Opted_In()
    {
        var queue = new RecordingJobQueue();
        var tenantSettings = new FakeTenantSettingsQueries(Company, Thresholds(tenantContact: true), Profile(TenantContact));
        var notifier = BuildNotifier(queue, tenantSettings, new SupervisionNotificationOptions { OperatorEmail = OperatorEmail });

        await notifier.NotifyRaisedAsync(RaiseAlert(AlertSeverity.Critical));

        queue.Emails.Should().HaveCount(2);
        queue.Emails.Should().Contain(e => e.RecipientEmail == OperatorEmail);
        queue.Emails.Should().Contain(e => e.RecipientEmail == TenantContact);
    }

    [Fact]
    public async Task Raise_Critical_Skips_Tenant_Contact_When_Opt_Out()
    {
        var queue = new RecordingJobQueue();
        var tenantSettings = new FakeTenantSettingsQueries(Company, Thresholds(tenantContact: false), Profile(TenantContact));
        var notifier = BuildNotifier(queue, tenantSettings, new SupervisionNotificationOptions { OperatorEmail = OperatorEmail });

        await notifier.NotifyRaisedAsync(RaiseAlert(AlertSeverity.Critical));

        queue.Emails.Should().ContainSingle(e => e.RecipientEmail == OperatorEmail);
        queue.Emails.Should().NotContain(e => e.RecipientEmail == TenantContact);
    }

    [Fact]
    public async Task Raise_Warning_Never_Emails_Tenant_Contact_Even_If_Opted_In()
    {
        var queue = new RecordingJobQueue();
        var tenantSettings = new FakeTenantSettingsQueries(Company, Thresholds(tenantContact: true), Profile(TenantContact));
        var notifier = BuildNotifier(queue, tenantSettings, new SupervisionNotificationOptions { OperatorEmail = OperatorEmail });

        await notifier.NotifyRaisedAsync(RaiseAlert(AlertSeverity.Warning));

        // Le contact tenant ne reçoit QUE les critiques (F12 §5.3).
        queue.Emails.Should().ContainSingle(e => e.RecipientEmail == OperatorEmail);
        queue.Emails.Should().NotContain(e => e.RecipientEmail == TenantContact);
    }

    [Fact]
    public async Task Raise_Critical_Skips_Tenant_Contact_When_Email_Missing()
    {
        var queue = new RecordingJobQueue();
        var tenantSettings = new FakeTenantSettingsQueries(Company, Thresholds(tenantContact: true), Profile(contactEmail: null));
        var notifier = BuildNotifier(queue, tenantSettings, new SupervisionNotificationOptions { OperatorEmail = OperatorEmail });

        await notifier.NotifyRaisedAsync(RaiseAlert(AlertSeverity.Critical));

        queue.Emails.Should().ContainSingle(e => e.RecipientEmail == OperatorEmail);
    }

    [Fact]
    public async Task Raise_Without_Operator_Email_Still_Emails_Tenant_Contact()
    {
        var queue = new RecordingJobQueue();
        var tenantSettings = new FakeTenantSettingsQueries(Company, Thresholds(tenantContact: true), Profile(TenantContact));
        var notifier = BuildNotifier(queue, tenantSettings, new SupervisionNotificationOptions { OperatorEmail = string.Empty });

        await notifier.NotifyRaisedAsync(RaiseAlert(AlertSeverity.Critical));

        queue.Emails.Should().ContainSingle(e => e.RecipientEmail == TenantContact);
    }

    [Fact]
    public async Task Resolution_Not_Sent_When_Disabled()
    {
        var queue = new RecordingJobQueue();
        var tenantSettings = new FakeTenantSettingsQueries(Company);
        var notifier = BuildNotifier(queue, tenantSettings, new SupervisionNotificationOptions
        {
            OperatorEmail = OperatorEmail,
            SendResolutionEmails = false,
        });

        var alert = RaiseAlert(AlertSeverity.Critical);
        alert.Resolve(Now.AddHours(1));

        await notifier.NotifyResolvedAsync(alert);

        queue.Emails.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolution_Sent_To_Operator_When_Enabled()
    {
        var queue = new RecordingJobQueue();
        var tenantSettings = new FakeTenantSettingsQueries(Company);
        var notifier = BuildNotifier(queue, tenantSettings, new SupervisionNotificationOptions
        {
            OperatorEmail = OperatorEmail,
            SendResolutionEmails = true,
        });

        var alert = RaiseAlert(AlertSeverity.Critical);
        alert.Resolve(Now.AddHours(1));

        await notifier.NotifyResolvedAsync(alert);

        queue.Emails.Should().ContainSingle(e => e.RecipientEmail == OperatorEmail);
        queue.Emails[0].Subject.Should().Contain("résolue");
    }

    [Fact]
    public async Task Notification_Is_Non_Blocking_When_Queue_Fails()
    {
        var tenantSettings = new FakeTenantSettingsQueries(Company);
        var notifier = new AlertEmailNotifier(
            new ThrowingJobQueue(),
            tenantSettings,
            new FakeAlertQueries(),
            Options.Create(new SupervisionNotificationOptions { OperatorEmail = OperatorEmail }),
            NullLogger<AlertEmailNotifier>.Instance);

        var act = async () => await notifier.NotifyRaisedAsync(RaiseAlert(AlertSeverity.Critical));

        // Échec de mise en file = journalisé, jamais propagé : ne casse pas l'évaluation (SUP03 §4).
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Digest_Does_Nothing_When_Disabled()
    {
        var queue = new RecordingJobQueue();
        var tenantSettings = new FakeTenantSettingsQueries(Company);
        var alertQueries = new FakeAlertQueries(ActiveDto(AlertSeverity.Critical));
        var notifier = BuildNotifier(queue, tenantSettings, new SupervisionNotificationOptions
        {
            OperatorEmail = OperatorEmail,
            DailyDigestEnabled = false,
        }, alertQueries);

        await notifier.SendActiveAlertsDigestAsync(Tenant);

        queue.Emails.Should().BeEmpty();
    }

    [Fact]
    public async Task Digest_Does_Nothing_When_No_Active_Alert()
    {
        var queue = new RecordingJobQueue();
        var tenantSettings = new FakeTenantSettingsQueries(Company);
        var notifier = BuildNotifier(queue, tenantSettings, new SupervisionNotificationOptions
        {
            OperatorEmail = OperatorEmail,
            DailyDigestEnabled = true,
        }, new FakeAlertQueries());

        await notifier.SendActiveAlertsDigestAsync(Tenant);

        queue.Emails.Should().BeEmpty();
    }

    [Fact]
    public async Task Digest_Sends_Active_Alerts_To_Operator_When_Enabled()
    {
        var queue = new RecordingJobQueue();
        var tenantSettings = new FakeTenantSettingsQueries(Company);
        var alertQueries = new FakeAlertQueries(
            ActiveDto(AlertSeverity.Critical, "agent.mute"),
            ActiveDto(AlertSeverity.Warning, "documents.blocked"));
        var notifier = BuildNotifier(queue, tenantSettings, new SupervisionNotificationOptions
        {
            OperatorEmail = OperatorEmail,
            DailyDigestEnabled = true,
        }, alertQueries);

        await notifier.SendActiveAlertsDigestAsync(Tenant);

        queue.Emails.Should().ContainSingle(e => e.RecipientEmail == OperatorEmail);
        queue.Emails[0].Body.Should().Contain("agent.mute").And.Contain("documents.blocked");
    }
}
