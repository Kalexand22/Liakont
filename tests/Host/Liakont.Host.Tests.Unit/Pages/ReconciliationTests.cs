namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Navigation;
using Liakont.Host.Reconciliation;
using Liakont.Host.Security;
using Liakont.Modules.Reconciliation.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Tests bUnit de la PAGE « Réconciliation des PDF » (WEB08) : état indisponible, chargement de la file
/// pour un opérateur, restriction sans permission, rechargement après action et bandeau d'erreur sur échec
/// de chargement. Le rendu détaillé est couvert par ReconciliationViewTests ; ici on prouve le wiring
/// page ↔ service ↔ permissions.
/// </summary>
public sealed class ReconciliationTests : BunitContext
{
    public ReconciliationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddLogging();
        Services.AddLocalization();

        Services.AddCommonUI();
        Services.AddSingleton<IActorContextAccessor>(new FakeActorContextAccessor());
        Services.AddScoped<IGridPreferenceService, NoOpGridPreferenceService>();
        Services.AddScoped<ISavedFilterService, NoOpSavedFilterService>();
    }

    [Fact]
    public void Unavailable_shows_unavailable_state()
    {
        var service = new FakeReconciliationConsoleService();
        Services.AddScoped<ILiakontConsoleContext>(_ => new FakeConsoleContext(reconciliationAvailable: false));
        Services.AddScoped<IReconciliationConsoleService>(_ => service);
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasActions: true));

        var cut = Render<Reconciliation>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='reconciliation-unavailable']").Should().ContainSingle());
        cut.FindAll("[data-testid='liakont-reconciliation']").Should().BeEmpty();
        service.GetQueueCalls.Should().Be(0);
    }

    [Fact]
    public void Available_with_actions_permission_loads_and_renders_the_queue()
    {
        var service = new FakeReconciliationConsoleService();
        Services.AddScoped<ILiakontConsoleContext>(_ => new FakeConsoleContext(reconciliationAvailable: true));
        Services.AddScoped<IReconciliationConsoleService>(_ => service);
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasActions: true));

        var cut = Render<Reconciliation>();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='liakont-reconciliation']").Should().ContainSingle();
            cut.FindAll("[data-testid='reconciliation-proposal']").Should().ContainSingle();
        });
        service.GetQueueCalls.Should().Be(1);
    }

    [Fact]
    public void Available_without_actions_permission_shows_restricted_and_does_not_query()
    {
        var service = new FakeReconciliationConsoleService();
        Services.AddScoped<ILiakontConsoleContext>(_ => new FakeConsoleContext(reconciliationAvailable: true));
        Services.AddScoped<IReconciliationConsoleService>(_ => service);
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasActions: false));

        var cut = Render<Reconciliation>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='reconciliation-restricted']").Should().ContainSingle());
        service.GetQueueCalls.Should().Be(0);
    }

    [Fact]
    public void Successful_action_reloads_the_queue()
    {
        var service = new FakeReconciliationConsoleService();
        Services.AddScoped<ILiakontConsoleContext>(_ => new FakeConsoleContext(reconciliationAvailable: true));
        Services.AddScoped<IReconciliationConsoleService>(_ => service);
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasActions: true));

        var cut = Render<Reconciliation>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='liakont-reconciliation']").Should().ContainSingle());

        cut.Find("[data-testid='reconciliation-confirm']").Click();

        cut.WaitForAssertion(() =>
        {
            service.ConfirmCalls.Should().Be(1);
            service.GetQueueCalls.Should().Be(2);
        });
    }

    [Fact]
    public void Load_failure_shows_error_banner()
    {
        var service = new FakeReconciliationConsoleService { ThrowOnGetQueue = true };
        Services.AddScoped<ILiakontConsoleContext>(_ => new FakeConsoleContext(reconciliationAvailable: true));
        Services.AddScoped<IReconciliationConsoleService>(_ => service);
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasActions: true));

        var cut = Render<Reconciliation>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='reconciliation-error']").Should().ContainSingle());
    }

    private sealed class FakeConsoleContext : ILiakontConsoleContext
    {
        public FakeConsoleContext(bool reconciliationAvailable) =>
            ReconciliationAvailable = reconciliationAvailable;

        public bool ReconciliationAvailable { get; }

        public int ReconciliationPendingCount => 0;

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeReconciliationConsoleService : IReconciliationConsoleService
    {
        public int GetQueueCalls { get; private set; }

        public int ConfirmCalls { get; private set; }

        public bool ThrowOnGetQueue { get; init; }

        public Task<ReconciliationQueueViewModel> GetQueueAsync(CancellationToken cancellationToken = default)
        {
            GetQueueCalls++;

            if (ThrowOnGetQueue)
            {
                throw new InvalidOperationException("Échec simulé du chargement de la file.");
            }

            return Task.FromResult(new ReconciliationQueueViewModel
            {
                Proposals =
                [
                    new ReconciliationProposalDto(
                        Guid.NewGuid(),
                        "pool",
                        "f.pdf",
                        Guid.NewGuid(),
                        "TextMatching",
                        "Medium",
                        "détail",
                        DateTimeOffset.UtcNow),
                ],
                Orphans = [],
                DocumentsWithoutPdf = [],
            });
        }

        public Task<ReconciliationActionResult> ConfirmProposalAsync(Guid queueEntryId, CancellationToken cancellationToken = default)
        {
            ConfirmCalls++;
            return Task.FromResult(ReconciliationActionResult.Ok("Proposition confirmée."));
        }

        public Task<ReconciliationActionResult> RejectProposalAsync(Guid queueEntryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ReconciliationActionResult.Ok("Rejetée."));

        public Task<ReconciliationActionResult> LinkManuallyAsync(
            Guid queueEntryId,
            Guid documentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ReconciliationActionResult.Ok("Lié."));
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly bool _hasActions;

        public FakePermissionService(bool hasActions) => _hasActions = hasActions;

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) =>
            _hasActions && string.Equals(permission, LiakontPermissions.Actions, StringComparison.Ordinal);
    }

    private sealed class FakeActorContextAccessor : IActorContextAccessor
    {
        public IActorContext Current { get; } = new AnonymousActorContext();

        private sealed class AnonymousActorContext : IActorContext
        {
            public Guid UserId => Guid.Empty;

            public Guid CorrelationId { get; } = Guid.NewGuid();

            public bool IsAuthenticated => false;

            public string? DisplayName => null;

            public string? Email => null;

            public Guid? CompanyId => null;

            public string? Timezone => null;

            public string? Language => null;

            public string? TenantId => null;
        }
    }

    private sealed class NoOpGridPreferenceService : IGridPreferenceService
    {
        public Task<UserGridPreference?> GetPreferenceAsync(Guid userId, string gridKey, CancellationToken ct = default) =>
            Task.FromResult<UserGridPreference?>(null);

        public Task SavePreferenceAsync(Guid userId, string gridKey, IReadOnlyList<string> columnKeys, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveViewPreferenceAsync(Guid userId, string gridKey, ViewKind viewKind, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveFilterStateAsync(Guid userId, string gridKey, string? filterStateJson, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveColumnWidthsAsync(Guid userId, string gridKey, IReadOnlyDictionary<string, string> columnWidths, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpSavedFilterService : ISavedFilterService
    {
        public Task<IReadOnlyList<SavedFilter>> ListAsync(Guid userId, string gridKey, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SavedFilter>>(Array.Empty<SavedFilter>());

        public Task<SavedFilter?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<SavedFilter?>(null);

        public Task<SavedFilter> SaveAsync(SavedFilter filter, CancellationToken ct = default) =>
            Task.FromResult(filter);

        public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SetDefaultAsync(Guid id, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
