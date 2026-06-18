namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.Reconciliation;
using Liakont.Modules.Reconciliation.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Tests bUnit du rendu PUR de la page « Réconciliation des PDF » (WEB08) : les trois files (propositions,
/// orphelins, documents sans PDF), leurs états vides, l'aperçu PDF natif (iframe sur l'endpoint API04), le
/// sélecteur de document du lien manuel, le masquage des files sans la permission opérateur, et les callbacks
/// d'action (confirmer / rejeter / aperçu / lien). La vue ne contient aucune logique : elle reçoit son modèle
/// et ses callbacks.
/// </summary>
public sealed class ReconciliationViewTests : BunitContext
{
    private static readonly Guid ProposalId = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
    private static readonly Guid OrphanId = Guid.Parse("bbbbbbbb-2222-4222-8222-222222222222");
    private static readonly Guid DocId = Guid.Parse("cccccccc-3333-4333-8333-333333333333");

    public ReconciliationViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
        Services.AddBrowserTimeZoneStub();
        Services.AddSingleton<IActorContextAccessor>(new FakeActorContextAccessor());
        Services.AddScoped<IGridPreferenceService, NoOpGridPreferenceService>();
        Services.AddScoped<ISavedFilterService, NoOpSavedFilterService>();
    }

    [Fact]
    public void Renders_three_sections_with_data()
    {
        var cut = Render<ReconciliationView>(p => p
            .Add(v => v.Model, FullModel())
            .Add(v => v.CanAct, true));

        cut.FindAll("[data-testid='reconciliation-proposals']").Should().ContainSingle();
        cut.FindAll("[data-testid='reconciliation-orphans']").Should().ContainSingle();
        cut.FindAll("[data-testid='reconciliation-without-pdf']").Should().ContainSingle();

        cut.FindAll("[data-testid='reconciliation-proposal']").Should().ContainSingle();
        cut.FindAll("[data-testid='reconciliation-orphan']").Should().ContainSingle();
        cut.FindAll("[data-testid='reconciliation-without-pdf-row']").Should().ContainSingle();

        // Le numéro du document proposé est résolu depuis la liste « sans PDF » (même DocId).
        cut.Markup.Should().Contain("FA-001");
    }

    [Fact]
    public void Empty_lists_show_explicit_empty_states()
    {
        var cut = Render<ReconciliationView>(p => p
            .Add(v => v.Model, EmptyModel())
            .Add(v => v.CanAct, true));

        cut.FindAll("[data-testid='reconciliation-proposals-empty']").Should().ContainSingle();
        cut.FindAll("[data-testid='reconciliation-orphans-empty']").Should().ContainSingle();
        cut.FindAll("[data-testid='reconciliation-without-pdf-empty']").Should().ContainSingle();
    }

    [Fact]
    public void Without_actions_permission_the_queues_are_hidden()
    {
        var cut = Render<ReconciliationView>(p => p
            .Add(v => v.Model, FullModel())
            .Add(v => v.CanAct, false));

        cut.FindAll("[data-testid='reconciliation-restricted']").Should().ContainSingle();
        cut.FindAll("[data-testid='reconciliation-proposals']").Should().BeEmpty();
        cut.FindAll("[data-testid='reconciliation-orphans']").Should().BeEmpty();
    }

    [Fact]
    public void Confirm_button_invokes_callback_with_entry_id()
    {
        Guid? confirmed = null;
        var cut = Render<ReconciliationView>(p => p
            .Add(v => v.Model, FullModel())
            .Add(v => v.CanAct, true)
            .Add(v => v.OnConfirm, (Guid id) => confirmed = id));

        cut.Find("[data-testid='reconciliation-confirm']").Click();

        confirmed.Should().Be(ProposalId);
    }

    [Fact]
    public void Reject_button_invokes_callback_with_entry_id()
    {
        Guid? rejected = null;
        var cut = Render<ReconciliationView>(p => p
            .Add(v => v.Model, FullModel())
            .Add(v => v.CanAct, true)
            .Add(v => v.OnReject, (Guid id) => rejected = id));

        cut.Find("[data-testid='reconciliation-reject']").Click();

        rejected.Should().Be(ProposalId);
    }

    [Fact]
    public void Preview_button_invokes_callback_and_iframe_renders_for_selected_entry()
    {
        Guid? previewed = null;
        var cut = Render<ReconciliationView>(p => p
            .Add(v => v.Model, FullModel())
            .Add(v => v.CanAct, true)
            .Add(v => v.OnPreview, (Guid id) => previewed = id));

        // Pas d'aperçu tant qu'aucune entrée n'est sélectionnée.
        cut.FindAll("[data-testid='reconciliation-pdf-frame']").Should().BeEmpty();

        cut.FindAll("[data-testid='reconciliation-preview']")[0].Click();
        previewed.Should().Be(ProposalId);

        // Avec l'entrée sélectionnée, l'iframe pointe l'endpoint d'affichage du PDF (WEB08/API04).
        var withPreview = Render<ReconciliationView>(p => p
            .Add(v => v.Model, FullModel())
            .Add(v => v.CanAct, true)
            .Add(v => v.PreviewEntryId, ProposalId));

        var frame = withPreview.Find("[data-testid='reconciliation-pdf-frame']");
        frame.GetAttribute("src").Should().Be($"/api/v1/reconciliation/{ProposalId}/pdf");
    }

    [Fact]
    public void Start_link_button_invokes_callback_for_orphan()
    {
        Guid? linking = null;
        var cut = Render<ReconciliationView>(p => p
            .Add(v => v.Model, FullModel())
            .Add(v => v.CanAct, true)
            .Add(v => v.OnStartLink, (Guid id) => linking = id));

        cut.Find("[data-testid='reconciliation-link-start']").Click();

        linking.Should().Be(OrphanId);
    }

    [Fact]
    public void Link_picker_lists_candidates_and_link_invokes_callback_with_orphan_and_document()
    {
        (Guid QueueEntryId, Guid DocumentId)? linked = null;
        var cut = Render<ReconciliationView>(p => p
            .Add(v => v.Model, FullModel())
            .Add(v => v.CanAct, true)
            .Add(v => v.LinkingEntryId, OrphanId)
            .Add(v => v.OnLink, ((Guid QueueEntryId, Guid DocumentId) req) => linked = req));

        // Le sélecteur est ouvert pour l'orphelin et liste le document candidat (« sans PDF »).
        cut.FindAll("[data-testid='reconciliation-link-picker']").Should().ContainSingle();
        cut.FindAll("[data-testid='reconciliation-link-pick']").Should().ContainSingle();

        cut.Find("[data-testid='reconciliation-link-pick']").Click();

        linked.Should().NotBeNull();
        linked!.Value.QueueEntryId.Should().Be(OrphanId);
        linked.Value.DocumentId.Should().Be(DocId);
    }

    [Fact]
    public void Action_message_is_displayed_as_alert_on_failure()
    {
        var cut = Render<ReconciliationView>(p => p
            .Add(v => v.Model, FullModel())
            .Add(v => v.CanAct, true)
            .Add(v => v.ActionMessage, "Entrée introuvable.")
            .Add(v => v.ActionFailed, true));

        var message = cut.Find("[data-testid='reconciliation-action-message']");
        message.TextContent.Should().Contain("Entrée introuvable.");
        message.GetAttribute("role").Should().Be("alert");
    }

    [Fact]
    public void Busy_disables_the_action_buttons()
    {
        var cut = Render<ReconciliationView>(p => p
            .Add(v => v.Model, FullModel())
            .Add(v => v.CanAct, true)
            .Add(v => v.Busy, true));

        cut.Find("[data-testid='reconciliation-confirm']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='reconciliation-reject']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='reconciliation-link-start']").HasAttribute("disabled").Should().BeTrue();
    }

    private static ReconciliationQueueViewModel FullModel() => new()
    {
        Proposals =
        [
            new ReconciliationProposalDto(
                QueueEntryId: ProposalId,
                PoolPdfId: "pool-1",
                FileName: "facture-001.pdf",
                ProposedDocumentId: DocId,
                Strategy: "TextMatching",
                Confidence: "Medium",
                Detail: "même date et même montant",
                CreatedUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero)),
        ],
        Orphans =
        [
            new OrphanPdfDto(
                QueueEntryId: OrphanId,
                PoolPdfId: "pool-2",
                FileName: "scan-vrac.pdf",
                Detail: "aucune correspondance",
                CreatedUtc: new DateTimeOffset(2026, 6, 2, 9, 0, 0, TimeSpan.Zero)),
        ],
        DocumentsWithoutPdf =
        [
            new DocumentWithoutPdfDto(DocId, "FA-001", new DateOnly(2026, 5, 1), 1200m),
        ],
    };

    private static ReconciliationQueueViewModel EmptyModel() => new()
    {
        Proposals = [],
        Orphans = [],
        DocumentsWithoutPdf = [],
    };

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
