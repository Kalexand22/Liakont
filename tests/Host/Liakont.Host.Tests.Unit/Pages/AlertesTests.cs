namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Alertes;
using Liakont.Host.Components.Pages;
using Liakont.Host.Supervision;
using Liakont.Modules.Supervision.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Page « Paramétrage › Alertes » (FIX210) : garde liakont.settings (accès refusé sans permission), échec de
/// chargement visible, et enregistrement des seuils / du contact câblé vers le service. L'absence de profil
/// remonte un message opérateur français lors de l'enregistrement du contact.
/// </summary>
public sealed class AlertesTests : BunitContext
{
    public AlertesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
        Services.AddScoped<ISupervisionLivenessProvider>(_ => new FakeLivenessProvider());
    }

    [Fact]
    public void Without_settings_permission_access_is_denied()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: false));
        Services.AddScoped<IAlertesConsoleService>(_ => new FakeAlertesService());

        var cut = Render<Alertes>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='alertes-denied']").Should().ContainSingle());
        cut.FindAll("[data-testid='alertes']").Should().BeEmpty();
    }

    [Fact]
    public void Load_failure_shows_a_visible_error_banner()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IAlertesConsoleService>(_ => new FakeAlertesService { ThrowOnLoad = true });

        var cut = Render<Alertes>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='alertes-error']").Should().ContainSingle());
        cut.FindAll("[data-testid='alertes']").Should().BeEmpty();
    }

    [Fact]
    public void With_permission_the_device_is_rendered()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IAlertesConsoleService>(_ => new FakeAlertesService());

        var cut = Render<Alertes>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='alertes']").Should().ContainSingle());
        cut.FindAll("[data-testid='alertes-rule-row']").Should().NotBeEmpty();
    }

    [Fact]
    public void Saving_thresholds_calls_the_service()
    {
        var fake = new FakeAlertesService();
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IAlertesConsoleService>(_ => fake);

        var cut = Render<Alertes>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='alertes-thresholds-save-btn']").Should().ContainSingle());
        cut.Find("[data-testid='alertes-thresholds-save-btn']").Click();

        cut.WaitForAssertion(() => fake.SaveThresholdsCalls.Should().Be(1));
    }

    [Fact]
    public void Saving_contact_calls_the_service()
    {
        var fake = new FakeAlertesService();
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IAlertesConsoleService>(_ => fake);

        var cut = Render<Alertes>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='alertes-contact-save-btn']").Should().ContainSingle());
        cut.Find("[data-testid='alertes-contact-save-btn']").Click();

        cut.WaitForAssertion(() => fake.SaveContactCalls.Should().Be(1));
    }

    [Fact]
    public void Missing_profile_on_contact_save_shows_a_french_error()
    {
        var fake = new FakeAlertesService { ThrowNotFoundOnContact = true };
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IAlertesConsoleService>(_ => fake);

        var cut = Render<Alertes>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='alertes-contact-save-btn']").Should().ContainSingle());
        cut.Find("[data-testid='alertes-contact-save-btn']").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='alertes-contact-feedback']").TextContent.Should().Contain("profil"));
    }

    private sealed class FakeAlertesService : IAlertesConsoleService
    {
        public bool ThrowOnLoad { get; init; }

        public bool ThrowNotFoundOnContact { get; init; }

        public int SaveThresholdsCalls { get; private set; }

        public int SaveContactCalls { get; private set; }

        public Task<AlertesViewModel> GetAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnLoad)
            {
                throw new InvalidOperationException("Échec simulé du chargement du dispositif d'alerte.");
            }

            return Task.FromResult(new AlertesViewModel
            {
                Device = new AlertDeviceStatusDto
                {
                    OperatorEmailConfigured = true,
                    EvaluationIntervalMinutes = 15,
                    Rules = new List<AlertRuleStatusDto>
                    {
                        new() { RuleKey = "agent.mute", DisplayName = "Agent muet", Severity = "Critique", IsActive = true, ThresholdDisplay = "> 24 h" },
                    },
                },
                Form = new AlertesFormModel { AgentSilentHours = 24, BlockedDocumentsDays = 5, PaRejectionsDays = 2 },
                ProfileExists = true,
            });
        }

        public Task SaveThresholdsAsync(AlertesThresholdInput input, CancellationToken cancellationToken = default)
        {
            SaveThresholdsCalls++;
            return Task.CompletedTask;
        }

        public Task SaveContactAsync(string? contactEmailAlerte, CancellationToken cancellationToken = default)
        {
            if (ThrowNotFoundOnContact)
            {
                throw new NotFoundException("TenantProfile", Guid.Empty);
            }

            SaveContactCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLivenessProvider : ISupervisionLivenessProvider
    {
        public Task<SupervisionLivenessView> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SupervisionLivenessView
            {
                Status = SupervisionLivenessStatus.Healthy,
                LastEvaluationUtc = new DateTimeOffset(2026, 6, 11, 11, 55, 0, TimeSpan.Zero),
                IntervalMinutes = 15,
            });
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly bool _hasSettings;

        public FakePermissionService(bool hasSettings) => _hasSettings = hasSettings;

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) =>
            _hasSettings && string.Equals(permission, "liakont.settings", StringComparison.Ordinal);
    }
}
