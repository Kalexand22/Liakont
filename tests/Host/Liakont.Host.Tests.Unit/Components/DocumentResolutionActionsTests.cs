namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.Documents;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Security;
using Xunit;

/// <summary>
/// Tests bUnit du composant d'actions de RÉSOLUTION TERMINALE (WEB03c) branché sur le détail document :
/// gating par permission (boutons masqués en lecture seule) et par état (traitement manuel : Bloqué/Rejeté ;
/// liaison au remplaçant : Rejeté), motif OBLIGATOIRE (confirmation bloquée tant qu'il est vide), sélecteur
/// de remplacement (candidats hors document courant, liaison sur sélection), et messages d'erreur français
/// sur refus (sans notifier de succès). Le service est remplacé par un faux : on prouve le WIRING UI ↔ service.
/// </summary>
public sealed class DocumentResolutionActionsTests : BunitContext
{
    private static readonly Guid DocId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public DocumentResolutionActionsTests()
    {
        // StratumButton (RadzenButton) peut appeler du JS : mode permissif, comme les autres tests de pages.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
    }

    [Fact]
    public void Without_actions_permission_no_resolution_region_is_shown()
    {
        Use(new FakeResolutionService(), hasActions: false);

        var cut = RenderActions(state: "RejectedByPa");

        cut.FindAll("[data-testid='document-resolution-actions']").Should().BeEmpty(
            "les actions mutantes sont masquées sans la permission liakont.actions (lecture seule)");
    }

    [Fact]
    public void Blocked_document_offers_only_manual_resolution()
    {
        Use(new FakeResolutionService(), hasActions: true);

        var cut = RenderActions(state: "Blocked");

        cut.FindAll("[data-testid='document-action-resolve-manually']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-action-supersede']").Should().BeEmpty(
            "la liaison à un remplaçant ne s'applique qu'à un document REJETÉ (port IDocumentLifecycle)");
    }

    [Fact]
    public void Rejected_document_offers_both_resolution_actions()
    {
        Use(new FakeResolutionService(), hasActions: true);

        var cut = RenderActions(state: "RejectedByPa");

        cut.FindAll("[data-testid='document-action-resolve-manually']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-action-supersede']").Should().ContainSingle();
    }

    [Fact]
    public void Issued_document_offers_no_resolution_even_with_permission()
    {
        Use(new FakeResolutionService(), hasActions: true);

        var cut = RenderActions(state: "Issued");

        cut.FindAll("[data-testid='document-resolution-actions']").Should().BeEmpty(
            "aucune résolution terminale ne s'applique à un document déjà émis");
    }

    [Fact]
    public void Manual_confirm_is_disabled_until_a_reason_is_entered()
    {
        Use(new FakeResolutionService(), hasActions: true);

        var cut = RenderActions(state: "Blocked");
        cut.Find("[data-testid='document-action-resolve-manually']").Click();

        cut.Find("[data-testid='document-resolve-manual-confirm']").HasAttribute("disabled").Should().BeTrue(
            "le motif est obligatoire : la confirmation est bloquée tant qu'il est vide");

        cut.Find("[data-testid='document-resolve-manual-reason']").Input("Avoir orphelin, traité hors passerelle.");

        cut.Find("[data-testid='document-resolve-manual-confirm']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Manual_resolution_calls_the_service_with_the_reason_and_notifies_on_success()
    {
        var service = new FakeResolutionService(resolveStatus: DocumentResolutionConsoleStatus.Succeeded);
        Use(service, hasActions: true);
        var resolved = 0;

        var cut = RenderActions(state: "Blocked", onResolved: () => resolved++);
        cut.Find("[data-testid='document-action-resolve-manually']").Click();
        cut.Find("[data-testid='document-resolve-manual-reason']").Input("Avoir orphelin, traité hors passerelle.");
        cut.Find("[data-testid='document-resolve-manual-confirm']").Click();

        service.LastResolveId.Should().Be(DocId);
        service.LastReason.Should().Be("Avoir orphelin, traité hors passerelle.");
        resolved.Should().Be(1, "après succès, la page est notifiée pour recharger le détail (état + historique)");
        cut.FindAll("[data-testid='document-resolve-manual-panel']").Should().BeEmpty("le panneau se ferme après succès");
    }

    [Fact]
    public void Manual_resolution_shows_a_french_error_and_does_not_notify_on_refusal()
    {
        Use(new FakeResolutionService(resolveStatus: DocumentResolutionConsoleStatus.InvalidState), hasActions: true);
        var resolved = 0;

        var cut = RenderActions(state: "Blocked", onResolved: () => resolved++);
        cut.Find("[data-testid='document-action-resolve-manually']").Click();
        cut.Find("[data-testid='document-resolve-manual-reason']").Input("Motif quelconque.");
        cut.Find("[data-testid='document-resolve-manual-confirm']").Click();

        cut.Find("[data-testid='document-resolve-manual-error']").TextContent.Should().Contain("n'est pas dans un état");
        resolved.Should().Be(0, "aucune notification de succès sur un refus");
    }

    [Fact]
    public void Supersede_loads_candidates_excluding_self_and_links_on_confirm()
    {
        var replacement = Candidate("2026-099", "MARTIN SAS");
        var service = new FakeResolutionService(
            supersedeStatus: DocumentResolutionConsoleStatus.Succeeded,
            candidates: new[] { replacement });
        Use(service, hasActions: true);
        var resolved = 0;

        var cut = RenderActions(state: "RejectedByPa", onResolved: () => resolved++);
        cut.Find("[data-testid='document-action-supersede']").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll($"[data-testid='document-supersede-candidate-{replacement.Id}']").Should().ContainSingle());
        service.LastSearchExcludedId.Should().Be(DocId, "la recherche exclut le document rejeté lui-même");

        cut.Find("[data-testid='document-supersede-confirm']").HasAttribute("disabled").Should().BeTrue(
            "la liaison est bloquée tant qu'aucun remplaçant n'est sélectionné");

        cut.Find($"[data-testid='document-supersede-candidate-{replacement.Id}']").Change(true);
        cut.Find("[data-testid='document-supersede-confirm']").Click();

        service.LastSupersedeId.Should().Be(DocId);
        service.LastReplacementId.Should().Be(replacement.Id);
        resolved.Should().Be(1);
    }

    [Fact]
    public void Supersede_with_no_candidate_shows_an_explicit_message()
    {
        Use(new FakeResolutionService(candidates: Array.Empty<DocumentReplacementCandidate>()), hasActions: true);

        var cut = RenderActions(state: "RejectedByPa");
        cut.Find("[data-testid='document-action-supersede']").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='document-supersede-empty']").Should().ContainSingle());
        cut.Find("[data-testid='document-supersede-confirm']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Supersede_shows_a_french_error_on_refusal()
    {
        var replacement = Candidate("2026-099", "MARTIN SAS");
        Use(
            new FakeResolutionService(
                supersedeStatus: DocumentResolutionConsoleStatus.ReplacementNotFound,
                candidates: new[] { replacement }),
            hasActions: true);

        var cut = RenderActions(state: "RejectedByPa");
        cut.Find("[data-testid='document-action-supersede']").Click();
        cut.WaitForAssertion(() =>
            cut.FindAll($"[data-testid='document-supersede-candidate-{replacement.Id}']").Should().ContainSingle());
        cut.Find($"[data-testid='document-supersede-candidate-{replacement.Id}']").Change(true);
        cut.Find("[data-testid='document-supersede-confirm']").Click();

        cut.Find("[data-testid='document-supersede-error']").TextContent.Should().Contain("introuvable dans ce tenant");
    }

    private static DocumentReplacementCandidate Candidate(string number, string customer) => new()
    {
        Id = Guid.NewGuid(),
        DocumentNumber = number,
        CustomerName = customer,
        IssueDate = new DateOnly(2026, 6, 2),
        TotalGross = 3410.00m,
        State = "Detected",
    };

    private void Use(IDocumentResolutionConsoleService service, bool hasActions)
    {
        Services.AddScoped<IDocumentResolutionConsoleService>(_ => service);
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasActions));
    }

    private IRenderedComponent<DocumentResolutionActions> RenderActions(string state, Action? onResolved = null) =>
        Render<DocumentResolutionActions>(p =>
        {
            p.Add(c => c.DocumentId, DocId);
            p.Add(c => c.DocumentNumber, "2026-002");
            p.Add(c => c.State, state);
            if (onResolved is not null)
            {
                p.Add(c => c.OnResolved, onResolved);
            }
        });

    private sealed class FakeResolutionService : IDocumentResolutionConsoleService
    {
        private readonly DocumentResolutionConsoleStatus _resolveStatus;
        private readonly DocumentResolutionConsoleStatus _supersedeStatus;
        private readonly IReadOnlyList<DocumentReplacementCandidate> _candidates;

        public FakeResolutionService(
            DocumentResolutionConsoleStatus resolveStatus = DocumentResolutionConsoleStatus.Succeeded,
            DocumentResolutionConsoleStatus supersedeStatus = DocumentResolutionConsoleStatus.Succeeded,
            IReadOnlyList<DocumentReplacementCandidate>? candidates = null)
        {
            _resolveStatus = resolveStatus;
            _supersedeStatus = supersedeStatus;
            _candidates = candidates ?? Array.Empty<DocumentReplacementCandidate>();
        }

        public Guid? LastResolveId { get; private set; }

        public string? LastReason { get; private set; }

        public Guid? LastSupersedeId { get; private set; }

        public Guid? LastReplacementId { get; private set; }

        public Guid? LastSearchExcludedId { get; private set; }

        public Task<DocumentResolutionConsoleStatus> ResolveManuallyAsync(
            Guid documentId, string? reason, CancellationToken cancellationToken = default)
        {
            LastResolveId = documentId;
            LastReason = reason;
            return Task.FromResult(_resolveStatus);
        }

        public Task<DocumentResolutionConsoleStatus> SupersedeAsync(
            Guid documentId, Guid replacementDocumentId, CancellationToken cancellationToken = default)
        {
            LastSupersedeId = documentId;
            LastReplacementId = replacementDocumentId;
            return Task.FromResult(_supersedeStatus);
        }

        public Task<IReadOnlyList<DocumentReplacementCandidate>> SearchReplacementCandidatesAsync(
            Guid rejectedDocumentId, string? search, CancellationToken cancellationToken = default)
        {
            LastSearchExcludedId = rejectedDocumentId;
            return Task.FromResult(_candidates);
        }
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
