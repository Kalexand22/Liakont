namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.InstanceEmail;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Page « Supervision › Configuration email » (ADR-0039) : garde liakont.instance.settings (accès refusé
/// sans permission), échec de chargement visible, enregistrement et envoi de test câblés vers le service,
/// champs OAuth révélés par le sélecteur de fournisseur, secrets masqués (jamais pré-remplis).
/// </summary>
public sealed class EmailInstanceTests : BunitContext
{
    public EmailInstanceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    [Fact]
    public void Without_instance_settings_permission_access_is_denied()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasInstanceSettings: false));
        Services.AddScoped<IInstanceEmailConfigService>(_ => new FakeService());

        var cut = Render<EmailInstance>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='email-denied']").Should().ContainSingle());
        cut.FindAll("[data-testid='email']").Should().BeEmpty();
    }

    [Fact]
    public void Load_failure_shows_a_visible_error_banner()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasInstanceSettings: true));
        Services.AddScoped<IInstanceEmailConfigService>(_ => new FakeService { ThrowOnLoad = true });

        var cut = Render<EmailInstance>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='email-error']").Should().ContainSingle());
        cut.FindAll("[data-testid='email']").Should().BeEmpty();
    }

    [Fact]
    public void With_permission_the_editor_is_rendered()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasInstanceSettings: true));
        Services.AddScoped<IInstanceEmailConfigService>(_ => new FakeService());

        var cut = Render<EmailInstance>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='email']").Should().ContainSingle());
        cut.FindAll("[data-testid='email-kind']").Should().ContainSingle();
    }

    [Fact]
    public void Saving_calls_the_service()
    {
        var fake = new FakeService();
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasInstanceSettings: true));
        Services.AddScoped<IInstanceEmailConfigService>(_ => fake);

        var cut = Render<EmailInstance>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='email-save-btn']").Should().ContainSingle());
        cut.Find("[data-testid='email-save-btn']").Click();

        cut.WaitForAssertion(() => fake.SaveCalls.Should().Be(1));
    }

    [Fact]
    public void Sending_a_test_email_calls_the_service_and_shows_feedback()
    {
        var fake = new FakeService { TestResult = EmailTestResult.Succeeded("Email de test envoyé à ops@test.") };
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasInstanceSettings: true));
        Services.AddScoped<IInstanceEmailConfigService>(_ => fake);

        var cut = Render<EmailInstance>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='email-test-btn']").Should().ContainSingle());
        cut.Find("[data-testid='email-test-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            fake.TestCalls.Should().Be(1);
            cut.Find("[data-testid='email-test-feedback']").TextContent.Should().Contain("envoyé");
        });
    }

    [Fact]
    public void Oauth_fields_are_revealed_only_for_an_oauth_provider()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasInstanceSettings: true));
        Services.AddScoped<IInstanceEmailConfigService>(_ => new FakeService());

        var cut = Render<EmailInstance>();

        // Défaut SmtpBasic : mot de passe SMTP visible, champs OAuth cachés.
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='email-smtp-password']").Should().ContainSingle());
        cut.FindAll("[data-testid='email-oauth-client-secret']").Should().BeEmpty();

        // Bascule sur Gmail : les champs OAuth apparaissent, le mot de passe SMTP disparaît.
        cut.Find("[data-testid='email-kind']").Change("GoogleOAuth2");

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='email-oauth-client-secret']").Should().ContainSingle();
            cut.FindAll("[data-testid='email-oauth-refresh-token']").Should().ContainSingle();
            cut.FindAll("[data-testid='email-smtp-password']").Should().BeEmpty();
        });
    }

    [Fact]
    public void A_stored_secret_is_masked_and_never_prefilled()
    {
        var fake = new FakeService
        {
            Model = new InstanceEmailConfigViewModel
            {
                Form = new InstanceEmailConfigForm { Kind = "SmtpBasic" },
                HasSmtpPassword = true,
            },
        };
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasInstanceSettings: true));
        Services.AddScoped<IInstanceEmailConfigService>(_ => fake);

        var cut = Render<EmailInstance>();

        cut.WaitForAssertion(() =>
        {
            var input = cut.Find("[data-testid='email-smtp-password']");

            // Jamais pré-rempli (le DTO n'expose pas le secret) ; un placeholder masqué signale « enregistré ».
            input.GetAttribute("value").Should().BeNullOrEmpty();
            input.GetAttribute("placeholder").Should().Contain("•");
        });
    }

    private sealed class FakeService : IInstanceEmailConfigService
    {
        private int _getCount;

        public bool ThrowOnLoad { get; init; }

        public InstanceEmailConfigViewModel Model { get; set; } =
            new() { Form = new InstanceEmailConfigForm() };

        public EmailTestResult TestResult { get; set; } = EmailTestResult.Succeeded("Envoyé.");

        public int SaveCalls { get; private set; }

        public int TestCalls { get; private set; }

        public Task<InstanceEmailConfigViewModel> GetAsync(CancellationToken cancellationToken = default)
        {
            _getCount++;
            if (ThrowOnLoad && _getCount == 1)
            {
                throw new InvalidOperationException("Échec simulé du chargement de la config email.");
            }

            return Task.FromResult(Model);
        }

        public Task SaveAsync(InstanceEmailConfigInput input, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }

        public Task<EmailTestResult> SendTestAsync(string recipient, CancellationToken cancellationToken = default)
        {
            TestCalls++;
            return Task.FromResult(TestResult);
        }
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly bool _hasInstanceSettings;

        public FakePermissionService(bool hasInstanceSettings) => _hasInstanceSettings = hasInstanceSettings;

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) =>
            _hasInstanceSettings && string.Equals(permission, "liakont.instance.settings", StringComparison.Ordinal);
    }
}
