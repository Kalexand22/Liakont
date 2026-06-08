namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Collections.Generic;
using System.Linq;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.Documents;
using Liakont.Modules.Documents.Contracts.DTOs;
using Microsoft.AspNetCore.Components;
using Xunit;

public sealed class DocumentDetailViewTests : BunitContext
{
    public DocumentDetailViewTests()
    {
        // StatusBadge / StratumTabs peuvent appeler du JS sur certaines interactions : mode permissif.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Should_Render_The_Four_Tabs()
    {
        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, BuildModel()));

        var tabs = cut.FindAll("button[role='tab']");
        tabs.Should().HaveCount(4);
        tabs.Select(t => t.TextContent.Trim()).Should()
            .Contain("Contenu").And.Contain("Contrôles").And.Contain("Historique").And.Contain("Archive");
    }

    [Fact]
    public void Should_Show_Header_And_Totals_On_The_Content_Tab()
    {
        var model = BuildModel(doc: Doc("2026-001", "Issued", customer: "DUPONT J.", siren: "123456782"));

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, model));

        cut.Find("[data-testid='document-detail-number']").TextContent.Should().Contain("2026-001");
        cut.Find("[data-testid='document-detail-type']").TextContent.Should().Contain("Facture");
        cut.Find("[data-testid='document-detail-customer']").TextContent.Should().Contain("DUPONT J.");
        cut.Find("[data-testid='document-detail-total-gross']").TextContent.Should().Contain("162,80");
        cut.FindAll("[data-testid='document-detail-state']").Should().NotBeEmpty();
    }

    [Fact]
    public void Should_Highlight_Blocking_Reason_On_Content_And_Controls_When_Blocked()
    {
        var model = BuildModel(doc: Doc("2026-002", "Blocked"), blockingReason: "Le SIREN de l'émetteur est invalide.");

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, model));

        // Onglet Contenu (par défaut) : la mise en évidence du blocage.
        cut.Find("[data-testid='document-detail-blocking']").TextContent.Should().Contain("Le SIREN de l'émetteur est invalide.");

        // Onglet Contrôles : le même motif, présenté comme contrôle échoué.
        SelectTab(cut, "Contrôles");
        cut.Find("[data-testid='document-detail-controls-blocked']").TextContent.Should().Contain("Le SIREN de l'émetteur est invalide.");
        cut.FindAll("[data-testid='document-detail-controls-ok']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Controls_Ok_When_Not_Blocked_Nor_Rejected()
    {
        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, BuildModel(doc: Doc("2026-003", "Issued"))));

        SelectTab(cut, "Contrôles");
        cut.FindAll("[data-testid='document-detail-controls-ok']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-controls-blocked']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-controls-rejected']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Flag_Blocked_Document_Even_When_The_Reason_Is_Empty()
    {
        // Un document Blocked SANS motif (MarkBlocked autorise reason=null) reste signalé comme bloqué —
        // jamais « Aucun contrôle en échec » sur un document réellement bloqué (CLAUDE.md n°12).
        var model = BuildModel(doc: Doc("2026-007", "Blocked"), blockingReason: null);

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, model));

        // Onglet Contenu : la mise en évidence du blocage est présente (message générique).
        cut.FindAll("[data-testid='document-detail-blocking']").Should().ContainSingle();

        // Onglet Contrôles : bloqué, jamais « OK ».
        SelectTab(cut, "Contrôles");
        cut.FindAll("[data-testid='document-detail-controls-blocked']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-controls-ok']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Rejected_Note_On_Controls_When_Rejected_By_Pa()
    {
        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, BuildModel(doc: Doc("2026-004", "RejectedByPa"))));

        SelectTab(cut, "Contrôles");
        cut.FindAll("[data-testid='document-detail-controls-rejected']").Should().ContainSingle();
        cut.Find("[data-testid='document-detail-controls-rejected']").TextContent.Should().Contain("Historique");
    }

    [Fact]
    public void Should_List_History_Events_With_French_Labels()
    {
        var events = new List<DocumentEventDto>
        {
            Event("DocumentDetected", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero)),
            Event("DocumentBlocked", new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero), detail: "Régime TVA non mappé.", op: "marie.compta"),
        };

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, BuildModel(events: events)));

        SelectTab(cut, "Historique");
        var rows = cut.FindAll("[data-testid='document-detail-event']");
        rows.Should().HaveCount(2);
        cut.Find("[data-testid='document-detail-history-list']").TextContent.Should()
            .Contain("Détecté").And.Contain("Bloqué").And.Contain("Régime TVA non mappé.").And.Contain("marie.compta")
            .And.Contain("01/06/2026 08:00 UTC");
    }

    [Fact]
    public void Should_Show_History_Empty_When_No_Events()
    {
        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, BuildModel(events: [])));

        SelectTab(cut, "Historique");
        cut.FindAll("[data-testid='document-detail-history-empty']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-history-list']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Render_Archive_Reference_And_Export_Link_When_Archived()
    {
        var archive = new ArchiveReferenceDto
        {
            PackagePath = "vault/2026/2026-001.zip",
            PackageHash = "sha256:aaa",
            ChainHash = "sha256:bbb",
            ArchivedUtc = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
        };

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, BuildModel(archive: archive, isArchived: true)));

        SelectTab(cut, "Archive");
        cut.FindAll("[data-testid='document-detail-archive-state']").Should().NotBeEmpty();
        cut.Find("[data-testid='document-detail-archive']").TextContent.Should().Contain("sha256:bbb");
        cut.FindAll("[data-testid='document-detail-archive-none']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-export']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Show_Archive_None_But_Still_Offer_Export_When_Not_Archived()
    {
        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, BuildModel(archive: null, isArchived: false)));

        SelectTab(cut, "Archive");
        cut.FindAll("[data-testid='document-detail-archive-none']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-export']").Should().ContainSingle();
    }

    [Fact]
    public void Export_Link_Should_Point_To_The_Audit_Export_Endpoint()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var model = BuildModel(doc: Doc("2026-006", "Issued", id: id));

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, model));

        SelectTab(cut, "Archive");
        var link = cut.Find("[data-testid='document-detail-export']");
        link.GetAttribute("href").Should().Be($"/api/v1/documents/{id}/audit-export");
        link.HasAttribute("download").Should().BeTrue("c'est un téléchargement de fichier, pas une navigation Blazor");
    }

    [Fact]
    public void Should_Show_Verdict_And_Recheck_When_CanAct_And_Blocked_With_Company_Hint()
    {
        var model = BuildModel(doc: Doc("2026-010", "Blocked", companyHint: true));

        var cut = Render<DocumentDetailView>(p => p
            .Add(v => v.Model, model)
            .Add(v => v.CanAct, true));

        SelectTab(cut, "Contrôles");
        cut.FindAll("[data-testid='document-detail-controls-actions']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-verdict-b2c']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-verdict-b2b']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-recheck']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Hide_All_Action_Buttons_When_Not_CanAct()
    {
        // Sans la permission d'action, la fiche reste consultable en lecture (WEB03a) : aucun bouton.
        var model = BuildModel(doc: Doc("2026-011", "Blocked", companyHint: true));

        var cut = Render<DocumentDetailView>(p => p
            .Add(v => v.Model, model)
            .Add(v => v.CanAct, false));

        SelectTab(cut, "Contrôles");
        cut.FindAll("[data-testid='document-detail-controls-actions']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-verdict-b2c']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-recheck']").Should().BeEmpty();

        // … mais le motif de blocage reste visible (lecture seule).
        cut.FindAll("[data-testid='document-detail-controls-blocked']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Hide_Verdict_But_Keep_Recheck_When_No_Company_Hint()
    {
        // Aucun indice « société » : le verdict B2B/B2C n'est pas proposé, mais la re-vérification reste offerte.
        var model = BuildModel(doc: Doc("2026-012", "Blocked", companyHint: false));

        var cut = Render<DocumentDetailView>(p => p
            .Add(v => v.Model, model)
            .Add(v => v.CanAct, true));

        SelectTab(cut, "Contrôles");
        cut.FindAll("[data-testid='document-detail-verdict-b2c']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-verdict-b2b']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-recheck']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Hide_Verdict_When_Already_Confirmed_B2c_But_Keep_Recheck()
    {
        // Verdict déjà posé : on ne le re-propose pas (mais on peut encore re-vérifier pour débloquer).
        var model = BuildModel(doc: Doc("2026-013", "Blocked", companyHint: true, confirmedB2c: true));

        var cut = Render<DocumentDetailView>(p => p
            .Add(v => v.Model, model)
            .Add(v => v.CanAct, true));

        SelectTab(cut, "Contrôles");
        cut.FindAll("[data-testid='document-detail-verdict-b2c']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-recheck']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Show_No_Action_Buttons_When_Document_Is_Not_Blocked()
    {
        var model = BuildModel(doc: Doc("2026-014", "Issued", companyHint: true));

        var cut = Render<DocumentDetailView>(p => p
            .Add(v => v.Model, model)
            .Add(v => v.CanAct, true));

        SelectTab(cut, "Contrôles");
        cut.FindAll("[data-testid='document-detail-controls-actions']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-recheck']").Should().BeEmpty();
    }

    [Fact]
    public void Action_Buttons_Invoke_Their_Callbacks()
    {
        var confirmed = false;
        var manual = false;
        var rechecked = false;
        var model = BuildModel(doc: Doc("2026-015", "Blocked", companyHint: true));

        var cut = Render<DocumentDetailView>(p => p
            .Add(v => v.Model, model)
            .Add(v => v.CanAct, true)
            .Add(v => v.OnConfirmB2c, EventCallback.Factory.Create(this, () => confirmed = true))
            .Add(v => v.OnHandleManually, EventCallback.Factory.Create(this, () => manual = true))
            .Add(v => v.OnRecheck, EventCallback.Factory.Create(this, () => rechecked = true)));

        SelectTab(cut, "Contrôles");
        cut.Find("[data-testid='document-detail-verdict-b2c']").Click();
        cut.Find("[data-testid='document-detail-verdict-b2b']").Click();
        cut.Find("[data-testid='document-detail-recheck']").Click();

        confirmed.Should().BeTrue("le bouton « Confirmer particulier (B2C) » déclenche son callback");
        manual.Should().BeTrue("le bouton « Traiter manuellement (B2B) » déclenche son callback");
        rechecked.Should().BeTrue("le bouton « Revérifier maintenant » déclenche son callback");
    }

    [Fact]
    public void Should_Render_Action_Feedback_With_Success_And_Error_Styling()
    {
        var model = BuildModel(doc: Doc("2026-016", "Blocked", companyHint: true));

        var ok = Render<DocumentDetailView>(p => p
            .Add(v => v.Model, model)
            .Add(v => v.CanAct, true)
            .Add(v => v.ActionFeedback, "Re-vérification réussie : le document est maintenant prêt à l'envoi.")
            .Add(v => v.ActionFailed, false));

        SelectTab(ok, "Contrôles");
        var okBanner = ok.Find("[data-testid='document-detail-action-feedback']");
        okBanner.TextContent.Should().Contain("prêt à l'envoi");
        okBanner.GetAttribute("class").Should().Contain("liakont-doc-detail__action-feedback--ok");

        var ko = Render<DocumentDetailView>(p => p
            .Add(v => v.Model, model)
            .Add(v => v.CanAct, true)
            .Add(v => v.ActionFeedback, "Document introuvable dans ce tenant.")
            .Add(v => v.ActionFailed, true));

        SelectTab(ko, "Contrôles");
        ko.Find("[data-testid='document-detail-action-feedback']")
            .GetAttribute("class").Should().Contain("liakont-doc-detail__action-feedback--error");
    }

    private static void SelectTab(IRenderedComponent<DocumentDetailView> cut, string title)
    {
        var tab = cut.FindAll("button[role='tab']")
            .Single(b => b.TextContent.Contains(title, StringComparison.Ordinal));
        tab.Click();
    }

    private static DocumentDetailViewModel BuildModel(
        DocumentDto? doc = null,
        IReadOnlyList<DocumentEventDto>? events = null,
        string? blockingReason = null,
        ArchiveReferenceDto? archive = null,
        bool isArchived = false) => new()
    {
        Document = doc ?? Doc("2026-000", "Issued"),
        Events = events ?? [],
        BlockingReason = blockingReason,
        Archive = archive,
        IsArchived = isArchived,
    };

    private static DocumentDto Doc(
        string number,
        string state,
        string customer = "DUPONT J.",
        string? siren = "123456782",
        Guid? id = null,
        bool companyHint = false,
        bool confirmedB2c = false) => new()
    {
        Id = id ?? Guid.NewGuid(),
        SourceReference = $"src/{number}",
        DocumentNumber = number,
        DocumentType = "invoice",
        IssueDate = new DateOnly(2026, 6, 1),
        SupplierSiren = siren,
        CustomerName = customer,
        CustomerIsCompanyHint = companyHint,
        BuyerConfirmedAsIndividual = confirmedB2c,
        TotalNet = 1000m,
        TotalTax = 162.80m,
        TotalGross = 1162.80m,
        State = state,
        PayloadHash = "sha256:payload",
        FirstSeenUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
        LastUpdateUtc = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
    };

    private static DocumentEventDto Event(string type, DateTimeOffset when, string? detail = null, string? op = null) => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = Guid.Empty,
        TimestampUtc = when,
        EventType = type,
        Detail = detail,
        OperatorIdentity = op,
    };
}
