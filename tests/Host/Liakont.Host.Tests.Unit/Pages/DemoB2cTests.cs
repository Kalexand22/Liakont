namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Demo;
using Liakont.Host.Documents;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Page « Démo e-reporting B2C — Essentiel » (B2C04) : échec de chargement visible, déclenchement manuel offert
/// SOUS permission d'action (liakont.actions) et câblé à la voie unique <see cref="IDocumentSendActions"/>,
/// message de retour restitué, et rechargement non bloquant en cas d'échec post-déclenchement. La page reste
/// présentationnelle (aucune logique métier) — ces branches sont prouvées avec des doubles.
/// </summary>
public sealed class DemoB2cTests : BunitContext
{
    public DemoB2cTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    [Fact]
    public void Load_failure_shows_a_visible_error_banner()
    {
        Register(new FakeDemoService { ThrowOnLoad = true }, new FakeSendActions(), canAct: true);

        var cut = Render<DemoB2c>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='demo-b2c-error']").Should().ContainSingle());
        cut.FindAll("[data-testid='demo-b2c']").Should().BeEmpty();
    }

    [Fact]
    public void Renders_The_View_When_Loaded()
    {
        Register(new FakeDemoService(), new FakeSendActions(), canAct: true);

        var cut = Render<DemoB2c>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='demo-b2c']").Should().ContainSingle());
    }

    [Fact]
    public void Trigger_Button_Offered_And_Calls_The_Single_Send_Path_When_CanAct()
    {
        var send = new FakeSendActions();
        Register(new FakeDemoService(), send, canAct: true);

        var cut = Render<DemoB2c>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='demo-b2c-trigger']").Should().ContainSingle());
        cut.Find("[data-testid='demo-b2c-trigger']").Click();

        cut.WaitForAssertion(() =>
        {
            send.TriggerCalls.Should().Be(1);
            cut.Find("[data-testid='demo-b2c-feedback']").TextContent.Should().Contain("Traitement déclenché");
        });
    }

    [Fact]
    public void Trigger_Button_Hidden_Without_Action_Permission()
    {
        Register(new FakeDemoService(), new FakeSendActions(), canAct: false);

        var cut = Render<DemoB2c>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='demo-b2c']").Should().ContainSingle());
        cut.FindAll("[data-testid='demo-b2c-trigger']").Should().BeEmpty();
    }

    [Fact]
    public void Trigger_Failure_Shows_A_French_Error_Message()
    {
        Register(new FakeDemoService(), new FakeSendActions { ThrowOnTrigger = true }, canAct: true);

        var cut = Render<DemoB2c>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='demo-b2c-trigger']").Should().ContainSingle());
        cut.Find("[data-testid='demo-b2c-trigger']").Click();

        cut.WaitForAssertion(() =>
        {
            var feedback = cut.Find("[data-testid='demo-b2c-feedback']");
            feedback.GetAttribute("role").Should().Be("alert");
            feedback.TextContent.Should().Contain("échoué");
        });
    }

    [Fact]
    public void Reload_failure_after_a_successful_trigger_keeps_the_result_message()
    {
        Register(new FakeDemoService { FailOnReload = true }, new FakeSendActions(), canAct: true);

        var cut = Render<DemoB2c>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='demo-b2c-trigger']").Should().ContainSingle());
        cut.Find("[data-testid='demo-b2c-trigger']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='demo-b2c-feedback']").TextContent.Should().Contain("Traitement déclenché");
            cut.FindAll("[data-testid='demo-b2c-error']").Should().BeEmpty();
            cut.FindAll("[data-testid='demo-b2c']").Should().ContainSingle();
        });
    }

    private void Register(FakeDemoService demo, FakeSendActions send, bool canAct)
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct));
        Services.AddScoped<IDemoB2cConsoleService>(_ => demo);
        Services.AddScoped<IDocumentSendActions>(_ => send);
    }

    private sealed class FakeDemoService : IDemoB2cConsoleService
    {
        private int _getCalls;

        public bool ThrowOnLoad { get; init; }

        public bool FailOnReload { get; init; }

        public Task<DemoB2cViewModel> GetAsync(CancellationToken cancellationToken = default)
        {
            _getCalls++;
            if (ThrowOnLoad && _getCalls == 1)
            {
                throw new InvalidOperationException("Échec simulé du chargement de la démo B2C.");
            }

            if (FailOnReload && _getCalls > 1)
            {
                throw new InvalidOperationException("Échec simulé du rechargement post-déclenchement.");
            }

            return Task.FromResult(new DemoB2cViewModel { Declarations = Array.Empty<DemoB2cDeclarationRow>() });
        }
    }

    private sealed class FakeSendActions : IDocumentSendActions
    {
        public bool ThrowOnTrigger { get; init; }

        public int TriggerCalls { get; private set; }

        public Task<DocumentSendActionResult> TriggerRunAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnTrigger)
            {
                throw new InvalidOperationException("Échec simulé du déclenchement.");
            }

            TriggerCalls++;
            return Task.FromResult(DocumentSendActionResult.Ok("Traitement déclenché pour le tenant."));
        }

        public Task<DocumentSendActionResult> SendSelectionAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentSendSummary> SummarizeReadyToSendAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentSendActionResult> SendAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly bool _canAct;

        public FakePermissionService(bool canAct) => _canAct = canAct;

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) =>
            _canAct && string.Equals(permission, "liakont.actions", StringComparison.Ordinal);
    }
}
