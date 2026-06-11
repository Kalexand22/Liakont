namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Documents;
using Liakont.Modules.Documents.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Security;
using Xunit;

/// <summary>
/// Tests bUnit du WIRING page détail document ↔ actions de résolution (WEB03c) : la page rend le composant
/// d'actions pour un document REJETÉ (porteur de la permission actions), et APRÈS une résolution réussie elle
/// RECHARGE le détail (l'état et la piste d'audit reflètent alors la résolution). Le rendu lecture des 4
/// onglets est couvert par <c>DocumentDetailViewTests</c> ; le comportement des actions par
/// <c>DocumentResolutionActionsTests</c> — ici on prouve l'assemblage page ↔ composant ↔ rechargement.
/// </summary>
public sealed class DocumentDetailResolutionWiringTests : BunitContext
{
    private static readonly Guid DocId = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000c3");

    public DocumentDetailResolutionWiringTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();

        // La page orchestre aussi les actions de la barre permanente (FIX04b) : IDocumentControlActions (verdict
        // garde-fou, re-vérification) ET IDocumentSendActions (envoi) doivent être résolvables pour la construire.
        // No-op ici (ces tests portent sur la région de résolution WEB03c, sur un document RejectedByPa — verdict
        // et re-vérification ne s'affichent que sur Blocked, l'envoi que sur ReadyToSend).
        Services.AddScoped<IDocumentControlActions>(_ => new NoOpControlActions());
        Services.AddScoped<IDocumentSendActions>(_ => new NoOpSendActions());
    }

    [Fact]
    public void Rejected_document_with_actions_permission_shows_the_resolution_region()
    {
        Services.AddScoped<IDocumentDetailConsoleQueries>(_ => FakeDetailQueries.Returning(RejectedModel()));
        Services.AddScoped<IDocumentResolutionConsoleService>(_ => new FakeResolutionService());
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasActions: true));

        var cut = Render<DocumentDetail>(p => p.Add(c => c.Id, DocId));

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='document-resolution-actions']").Should().ContainSingle());
        cut.FindAll("[data-testid='document-action-supersede']").Should().ContainSingle(
            "un document rejeté propose la liaison à un remplaçant");
    }

    [Fact]
    public void After_a_successful_manual_resolution_the_page_reloads_the_detail()
    {
        // Le service rend un document REJETÉ au 1er chargement, puis TRAITÉ MANUELLEMENT au rechargement :
        // après l'action, la page recharge → l'état change → la région d'actions disparaît (état terminal).
        var queries = FakeDetailQueries.Sequence(RejectedModel(), ManuallyHandledModel());
        Services.AddScoped<IDocumentDetailConsoleQueries>(_ => queries);
        Services.AddScoped<IDocumentResolutionConsoleService>(_ => new FakeResolutionService(DocumentResolutionConsoleStatus.Succeeded));
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasActions: true));

        var cut = Render<DocumentDetail>(p => p.Add(c => c.Id, DocId));

        cut.Find("[data-testid='document-action-resolve-manually']").Click();
        cut.Find("[data-testid='document-resolve-manual-reason']").Input("Avoir orphelin, traité hors passerelle.");
        cut.Find("[data-testid='document-resolve-manual-confirm']").Click();

        cut.WaitForAssertion(() =>
            queries.Calls.Should().Be(2, "la page recharge le détail après une résolution réussie"));
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='document-resolution-actions']").Should().BeEmpty(
                "après traitement manuel (état terminal), aucune action de résolution ne subsiste"));
    }

    private static DocumentDetailViewModel RejectedModel() => Model("RejectedByPa");

    private static DocumentDetailViewModel ManuallyHandledModel() => Model("ManuallyHandled");

    private static DocumentDetailViewModel Model(string state) => new()
    {
        Document = new DocumentDto
        {
            Id = DocId,
            SourceReference = "src/2026-002",
            DocumentNumber = "2026-002",
            DocumentType = "invoice",
            IssueDate = new DateOnly(2026, 6, 1),
            SupplierSiren = "123456782",
            CustomerName = "MARTIN SAS",
            CustomerIsCompanyHint = false,
            TotalNet = 2800m,
            TotalTax = 560m,
            TotalGross = 3360m,
            State = state,
            PayloadHash = "sha256:payload",
            FirstSeenUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            LastUpdateUtc = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
        },
        Events = Array.Empty<DocumentEventDto>(),
        BlockingReason = null,
        Archive = null,
        IsArchived = false,
    };

    private sealed class FakeDetailQueries : IDocumentDetailConsoleQueries
    {
        private readonly IReadOnlyList<DocumentDetailViewModel> _models;

        private FakeDetailQueries(IReadOnlyList<DocumentDetailViewModel> models) => _models = models;

        public int Calls { get; private set; }

        public static FakeDetailQueries Returning(DocumentDetailViewModel model) => new(new[] { model });

        public static FakeDetailQueries Sequence(params DocumentDetailViewModel[] models) => new(models);

        public Task<DocumentDetailViewModel?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
        {
            // Reporte le modèle de l'appel courant ; au-delà de la séquence, conserve le dernier (état stable).
            var index = Math.Min(Calls, _models.Count - 1);
            Calls++;
            return Task.FromResult<DocumentDetailViewModel?>(_models[index]);
        }
    }

    private sealed class FakeResolutionService : IDocumentResolutionConsoleService
    {
        private readonly DocumentResolutionConsoleStatus _status;

        public FakeResolutionService(DocumentResolutionConsoleStatus status = DocumentResolutionConsoleStatus.Succeeded) =>
            _status = status;

        public Task<DocumentResolutionConsoleStatus> ResolveManuallyAsync(
            Guid documentId, string? reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);

        public Task<DocumentResolutionConsoleStatus> SupersedeAsync(
            Guid documentId, Guid replacementDocumentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);

        public Task<IReadOnlyList<DocumentReplacementCandidate>> SearchReplacementCandidatesAsync(
            Guid rejectedDocumentId, string? search, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DocumentReplacementCandidate>>(Array.Empty<DocumentReplacementCandidate>());
    }

    private sealed class NoOpControlActions : IDocumentControlActions
    {
        public Task<DocumentControlActionResult> SubmitVerdictAsync(Guid documentId, ConsoleVerdict verdict, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentControlActionResult.Ok("ok", "Blocked"));

        public Task<DocumentControlActionResult> RecheckAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentControlActionResult.Ok("ok", "Blocked"));
    }

    private sealed class NoOpSendActions : IDocumentSendActions
    {
        public Task<DocumentSendActionResult> SendSelectionAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentSendActionResult.Ok("ok"));

        public Task<DocumentSendSummary> SummarizeReadyToSendAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DocumentSendSummary(0, 0m));

        public Task<DocumentSendActionResult> SendAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentSendActionResult.Ok("ok"));

        public Task<DocumentSendActionResult> TriggerRunAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentSendActionResult.Ok("ok"));
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
            _hasActions && string.Equals(permission, "liakont.actions", StringComparison.Ordinal);
    }
}
