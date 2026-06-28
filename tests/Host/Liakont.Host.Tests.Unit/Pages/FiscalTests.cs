namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.BillingMentions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Fiscal;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Page « Paramétrage › Fiscal » (FIX301) : garde liakont.settings (accès refusé sans permission), échec de
/// chargement visible, enregistrement câblé vers le service, et rejet d'une valeur inconnue par le handler
/// remonté en message opérateur français (jamais de valeur devinée).
/// </summary>
public sealed class FiscalTests : BunitContext
{
    public FiscalTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();

        // La page Fiscal rend aussi la section « Mentions de facturation » (BUG-26, intégrées à la page
        // fiscale) : un faux service suffit pour faire rendre la vue dans les tests du paramétrage fiscal.
        Services.AddScoped<IBillingMentionsConsoleService>(_ => new FakeBillingMentionsService());
    }

    [Fact]
    public void Without_settings_permission_access_is_denied()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: false));
        Services.AddScoped<IFiscalConsoleService>(_ => new FakeFiscalService());

        var cut = Render<Fiscal>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='fiscal-denied']").Should().ContainSingle());
        cut.FindAll("[data-testid='fiscal']").Should().BeEmpty();
    }

    [Fact]
    public void Load_failure_shows_a_visible_error_banner()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IFiscalConsoleService>(_ => new FakeFiscalService { ThrowOnLoad = true });

        var cut = Render<Fiscal>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='fiscal-error']").Should().ContainSingle());
        cut.FindAll("[data-testid='fiscal']").Should().BeEmpty();
    }

    [Fact]
    public void With_permission_the_editor_is_rendered()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IFiscalConsoleService>(_ => new FakeFiscalService());

        var cut = Render<Fiscal>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='fiscal']").Should().ContainSingle());
        cut.FindAll("[data-testid='fiscal-operation-category']").Should().ContainSingle();
    }

    [Fact]
    public void Billing_mentions_section_is_rendered_within_the_fiscal_page()
    {
        // BUG-26 (ajustement PO) : les mentions de facturation sont intégrées à la page « Paramètres
        // fiscaux » — la section et son formulaire sont rendus SOUS le formulaire fiscal.
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IFiscalConsoleService>(_ => new FakeFiscalService());

        var cut = Render<Fiscal>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='fiscal']").Should().ContainSingle());
        cut.FindAll("[data-testid='mentions']").Should().ContainSingle();
        cut.FindAll("[data-testid='mentions-save-btn']").Should().ContainSingle();
    }

    [Fact]
    public void Saving_calls_the_service()
    {
        var fake = new FakeFiscalService();
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IFiscalConsoleService>(_ => fake);

        var cut = Render<Fiscal>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='fiscal-save-btn']").Should().ContainSingle());
        cut.Find("[data-testid='fiscal-save-btn']").Click();

        cut.WaitForAssertion(() => fake.SaveCalls.Should().Be(1));
    }

    [Fact]
    public void Unknown_value_rejection_shows_a_french_error()
    {
        var fake = new FakeFiscalService { ThrowArgumentOnSave = true };
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IFiscalConsoleService>(_ => fake);

        var cut = Render<Fiscal>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='fiscal-save-btn']").Should().ContainSingle());
        cut.Find("[data-testid='fiscal-save-btn']").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='fiscal-feedback']").TextContent.Should().Contain("non reconnue"));
    }

    [Fact]
    public void Reload_failure_after_successful_save_keeps_the_success_message()
    {
        var fake = new FakeFiscalService { FailOnReload = true };
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IFiscalConsoleService>(_ => fake);

        var cut = Render<Fiscal>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='fiscal-save-btn']").Should().ContainSingle());
        cut.Find("[data-testid='fiscal-save-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='fiscal-feedback']").TextContent.Should().Contain("enregistrés");
            cut.FindAll("[data-testid='fiscal-error']").Should().BeEmpty();
            cut.FindAll("[data-testid='fiscal']").Should().ContainSingle();
        });
    }

    private sealed class FakeFiscalService : IFiscalConsoleService
    {
        private int _getCallCount;

        public bool ThrowOnLoad { get; init; }

        public bool ThrowArgumentOnSave { get; init; }

        public bool FailOnReload { get; init; }

        public int SaveCalls { get; private set; }

        public Task<FiscalViewModel> GetAsync(CancellationToken cancellationToken = default)
        {
            _getCallCount++;

            if (ThrowOnLoad && _getCallCount == 1)
            {
                throw new InvalidOperationException("Échec simulé du chargement du paramétrage fiscal.");
            }

            if (FailOnReload && _getCallCount > 1)
            {
                throw new InvalidOperationException("Échec simulé du rechargement post-enregistrement.");
            }

            return Task.FromResult(new FiscalViewModel
            {
                Form = new FiscalFormModel
                {
                    VatOnDebits = string.Empty,
                    OperationCategory = string.Empty,
                    FeeImputationMethod = string.Empty,
                    ReportingFrequency = string.Empty,
                },
                OperationCategoryOptions = FiscalSettingsOptions.OperationCategories,
                FeeImputationMethodOptions = FiscalSettingsOptions.FeeImputationMethods,
            });
        }

        public Task SaveAsync(FiscalSettingsInput input, CancellationToken cancellationToken = default)
        {
            if (ThrowArgumentOnSave)
            {
                throw new ArgumentException("Valeur inconnue simulée.");
            }

            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBillingMentionsService : IBillingMentionsConsoleService
    {
        public Task<BillingMentionsViewModel> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BillingMentionsViewModel
            {
                Form = new BillingMentionsFormModel
                {
                    PaymentTerms = string.Empty,
                    LatePenaltyTerms = string.Empty,
                    RecoveryFeeTerms = string.Empty,
                    DiscountTerms = string.Empty,
                },
            });

        public Task SaveAsync(BillingMentionsInput input, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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
