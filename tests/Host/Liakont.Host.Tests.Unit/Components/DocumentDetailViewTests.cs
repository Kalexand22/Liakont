namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Collections.Generic;
using System.Linq;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.Documents;
using Liakont.Modules.Documents.Contracts.DTOs;
using Xunit;

// Depuis FIX04b, DocumentDetailView ne porte plus AUCUNE action : les onglets sont du CONTENU seul
// (Contenu / Contrôles / Historique / Archive). Les actions (verdict garde-fou, re-vérification, envoi)
// sont testées sur la barre d'actions permanente — voir DocumentActionBarTests. Ce fichier ne couvre donc
// que le rendu en lecture de la vue.
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
        cut.Find("[data-testid='document-detail-total-gross']").TextContent.Should().Contain("162,80")
            .And.Contain("€", "les montants affichent la devise (retour de recette lot 2)");
        cut.FindAll("[data-testid='document-detail-state']").Should().NotBeEmpty();
    }

    [Fact]
    public void Should_Render_Line_Detail_Table_On_Content_Tab_With_Mapping_Result()
    {
        // FIX205 : les lignes du document transmis sont VISIBLES à l'écran (jamais du JSON) — libellé, montant HT,
        // régime source → catégorie/VATEX résultante du mapping, taux. Les lignes somment aux totaux de l'en-tête.
        var lines = new[]
        {
            Line("Vente principale", netAmount: 900m, category: "S — Taux normal", sourceRegime: "FR-STD", taxAmount: 150m, rate: 20m),
            Line("Frais de port", netAmount: 100m, category: "AA — Taux réduit", sourceRegime: "FR-RED", taxAmount: 12.80m, rate: 10m),
        };
        var model = BuildModel(doc: Doc("2026-010", "Issued"), content: Content(lines));

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, model));

        cut.FindAll("[data-testid='document-detail-lines']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-line']").Should().HaveCount(2);

        var table = cut.Find("[data-testid='document-detail-lines']").TextContent;
        table.Should().Contain("Désignation").And.Contain("Montant HT").And.Contain("Catégorie TVA")
            .And.Contain("Régime source").And.Contain("VATEX");
        table.Should().Contain("Vente principale").And.Contain("900,00")
            .And.Contain("S — Taux normal").And.Contain("FR-STD").And.Contain("20 %")
            .And.Contain("Frais de port").And.Contain("AA — Taux réduit").And.Contain("10 %");

        // Aucun JSON brut affiché (F10 §1).
        table.Should().NotContain("{").And.NotContain("CategoryCode");

        // Totaux cohérents (900 + 100 = 1000 HT ; 150 + 12,80 = 162,80 TVA).
        cut.FindAll("[data-testid='document-detail-lines-coherent']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-lines-mismatch']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Render_Lines_With_Source_Regime_For_A_Blocked_Document_Even_Without_Mapping()
    {
        // BUG-5 : un document BLOQUÉ (mapping non abouti) montre tout de même ses lignes — projetées au read-time
        // depuis le pivot SOURCE relu — avec le RÉGIME SOURCE lu mais une catégorie/un VATEX VIDES (« — »).
        // C'est le diagnostic FACTUEL du blocage (on voit ce qui a été lu et que la classification n'a pas abouti),
        // jamais une catégorie inventée. Légitime pour un Bloqué (≠ Prêt-à-envoyer où le mapping a réussi).
        var lines = new[]
        {
            Line("Adjudication lot 12", netAmount: 500m, category: "—", sourceRegime: "6", vatex: "—", taxAmount: null, rate: null),
        };
        var model = BuildModel(doc: Doc("2026-014", "Blocked"), content: Content(lines, totals: Check()));

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, model));

        // Les lignes sont visibles dès l'état Bloqué — pas la note d'absence.
        cut.FindAll("[data-testid='document-detail-lines']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-line']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-lines-empty']").Should().BeEmpty();

        var table = cut.Find("[data-testid='document-detail-lines']").TextContent;
        table.Should().Contain("Adjudication lot 12").And.Contain("500,00")
            .And.Contain("6", "le régime source lu est restitué pour diagnostiquer le blocage");

        // Aucune catégorie/VATEX inventés : la cellule reste « — » (le mapping n'a pas abouti).
        var cells = cut.FindAll("[data-testid='document-detail-line'] td");
        cells[4].TextContent.Trim().Should().Be("—", "catégorie TVA vide tant que le mapping n'a pas abouti");
        cells[5].TextContent.Trim().Should().Be("—", "VATEX vide tant que le mapping n'a pas abouti");
    }

    [Fact]
    public void Should_Show_Lines_Note_When_No_Lines_Are_Available()
    {
        // Aucune ligne RÉELLEMENT disponible (contenu source indisponible ET aucun pivot transmis) : pas de
        // tableau, une note honnête — jamais de ligne inventée. (Le cas nominal, lui, montre les lignes au read-time.)
        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, BuildModel(doc: Doc("2026-011", "Blocked"))));

        cut.FindAll("[data-testid='document-detail-lines-empty']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-lines']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-line']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-lines-coherent']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Flag_Totals_Mismatch_Between_Header_And_Lines()
    {
        // Contrôle de cohérence S2.5 : un contrôle net incohérent (calculé en amont par la projection) est REND
        // comme une alerte d'écart (jamais corrigé). La vue rend le verdict fourni, elle ne le recalcule pas.
        var lines = new[] { Line("Ligne unique", netAmount: 500m, category: "S — Taux normal", taxAmount: 100m) };
        var model = BuildModel(doc: Doc("2026-012", "Issued"), content: Content(lines, totals: Check(netConsistent: false)));

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, model));

        cut.FindAll("[data-testid='document-detail-lines-mismatch']").Should().ContainSingle();
        cut.Find("[data-testid='document-detail-lines-mismatch']").TextContent.Should().Contain("Écart");
        cut.FindAll("[data-testid='document-detail-lines-coherent']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Render_Document_Charges_When_Present()
    {
        // FIX205 (P1 review) : les charges/remises de NIVEAU DOCUMENT (éco-contribution, remise globale) sont
        // affichées à part, et le verdict de cohérence en tient compte (« lignes et charges concordent »).
        var lines = new[] { Line("Vente", netAmount: 900m, category: "S — Taux normal", taxAmount: 180m) };
        var charges = new[] { Charge("éco-contribution", isCharge: true, amount: 100m) };
        var model = BuildModel(doc: Doc("2026-013", "Issued"), content: Content(lines, charges: charges));

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, model));

        cut.FindAll("[data-testid='document-detail-charges']").Should().ContainSingle();
        var chargeRows = cut.FindAll("[data-testid='document-detail-charge']");
        chargeRows.Should().ContainSingle();
        cut.Find("[data-testid='document-detail-charges']").TextContent.Should().Contain("éco-contribution").And.Contain("Charge");
        cut.Find("[data-testid='document-detail-lines-coherent']").TextContent.Should().Contain("et charges");
    }

    [Fact]
    public void Should_Render_Billing_Mentions_With_French_Labels_On_The_Content_Tab()
    {
        // BUG-26 (F12-A §3.4) : les mentions de facturation EFFECTIVES du document sont restituées dans une carte
        // dédiée — termes de paiement (BT-20) + les 3 mentions légales FR mappées en libellé français depuis leur
        // code sujet (PMD → « Pénalités de retard », PMT → « Indemnité forfaitaire de recouvrement », AAB →
        // « Escompte / absence d'escompte »). Restitution LISIBLE, jamais de JSON (F10 §1) ni de code brut.
        var notes = new[]
        {
            Note("Pénalités de retard au taux légal.", "PMD"),
            Note("Indemnité forfaitaire de recouvrement de 40 €.", "PMT"),
            Note("Pas d'escompte pour paiement anticipé.", "AAB"),
        };
        var content = Content(
            [Line("Vente", netAmount: 1000m, category: "S — Taux normal", taxAmount: 162.80m)],
            paymentTerms: "Paiement à 30 jours fin de mois.",
            notes: notes);
        var model = BuildModel(doc: Doc("2026-020", "Issued"), content: content);

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, model));

        var card = cut.Find("[data-testid='document-detail-mentions']").TextContent;
        cut.Find("[data-testid='document-detail-payment-terms']").TextContent.Should().Contain("Paiement à 30 jours fin de mois.");

        cut.FindAll("[data-testid='document-detail-mention']").Should().HaveCount(3);
        card.Should().Contain("Pénalités de retard").And.Contain("Pénalités de retard au taux légal.")
            .And.Contain("Indemnité forfaitaire de recouvrement").And.Contain("Indemnité forfaitaire de recouvrement de 40 €.")
            .And.Contain("Escompte / absence d'escompte").And.Contain("Pas d'escompte pour paiement anticipé.");

        // Le code sujet brut (PMD/PMT/AAB) n'apparaît pas en clair : seul le libellé français est affiché.
        card.Should().NotContain("PMD").And.NotContain("PMT").And.NotContain("AAB");

        // Aucune note d'absence quand des mentions sont portées.
        cut.FindAll("[data-testid='document-detail-mentions-empty']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Mentions_Hint_When_No_Billing_Mention_Is_Carried()
    {
        // BUG-26 : un document sans mention (ni termes de paiement ni note) affiche un hint honnête, jamais une
        // mention inventée (CLAUDE.md n°2). Le défaut tenant non paramétré reste vide → hint.
        var content = Content([Line("Vente", netAmount: 1000m, category: "S — Taux normal", taxAmount: 162.80m)]);
        var model = BuildModel(doc: Doc("2026-021", "Issued"), content: content);

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, model));

        cut.FindAll("[data-testid='document-detail-mentions-empty']").Should().ContainSingle();
        cut.Find("[data-testid='document-detail-mentions-empty']").TextContent.Should()
            .Contain("Aucune mention de facturation paramétrée");
        cut.FindAll("[data-testid='document-detail-mention']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-payment-terms']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Render_Payment_Terms_Even_When_No_Legal_Notes_Are_Carried()
    {
        // Mentions partielles : seuls les termes de paiement sont portés (les 3 notes légales absentes). La carte
        // s'affiche (termes présents), sans note légale — jamais de note inventée.
        var content = Content(
            [Line("Vente", netAmount: 1000m, category: "S — Taux normal", taxAmount: 162.80m)],
            paymentTerms: "Paiement comptant à réception.");
        var model = BuildModel(doc: Doc("2026-022", "Issued"), content: content);

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, model));

        cut.Find("[data-testid='document-detail-payment-terms']").TextContent.Should().Contain("Paiement comptant à réception.");
        cut.FindAll("[data-testid='document-detail-mention']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-mentions-empty']").Should().BeEmpty();
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
    public void Controls_Tab_Carries_No_Action_Buttons()
    {
        // Garde anti-régression FIX04b : l'onglet Contrôles ne porte QUE du contenu — aucune action (verdict,
        // re-vérification, envoi) n'y est accessible ; les actions vivent dans la barre permanente en tête.
        var model = BuildModel(doc: Doc("2026-009", "Blocked", companyHint: true));

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, model));

        SelectTab(cut, "Contrôles");
        cut.FindAll("[data-testid='document-detail-controls-actions']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-verdict-b2c']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-verdict-b2b']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-recheck']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-send']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-action-feedback']").Should().BeEmpty();
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
    public void History_Should_Show_Operator_By_Name_With_The_Guid_In_The_Tooltip()
    {
        // FIX305 : un événement portant un NOM persisté l'affiche (« par Marie Comptable ») ; le GUID brut
        // n'apparaît PAS dans le texte, mais reste disponible en détail technique (infobulle title).
        var guid = "30da7398-1111-2222-3333-444455556666";
        var events = new List<DocumentEventDto>
        {
            Event("DocumentRecheckedStillBlocked", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), detail: "Acheteur professionnel non confirmé.", op: guid, opName: "Marie Comptable"),
        };

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, BuildModel(events: events)));

        SelectTab(cut, "Historique");
        var operatorSpan = cut.Find("[data-testid='document-detail-event'] .liakont-doc-detail__event-operator");
        operatorSpan.TextContent.Should().Contain("Marie Comptable").And.NotContain(guid);
        operatorSpan.GetAttribute("title").Should().Be(guid, "le GUID reste le détail technique en infobulle");
    }

    [Fact]
    public void History_Should_Fall_Back_To_A_Neutral_Account_Mention_For_Legacy_Events_Without_A_Name()
    {
        // FIX305 : un événement ANTÉRIEUR (sans nom persisté) ne doit pas afficher un GUID brut illisible —
        // repli sur une mention neutre « compte 30da7398… », le GUID complet restant en infobulle. Append-only :
        // l'événement ancien n'est jamais réécrit, c'est la RESTITUTION qui retombe proprement.
        var guid = "30da7398-1111-2222-3333-444455556666";
        var events = new List<DocumentEventDto>
        {
            Event("DocumentManuallyHandled", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), detail: "Traité manuellement.", op: guid, opName: null),
        };

        var cut = Render<DocumentDetailView>(p => p.Add(v => v.Model, BuildModel(events: events)));

        SelectTab(cut, "Historique");
        var operatorSpan = cut.Find("[data-testid='document-detail-event'] .liakont-doc-detail__event-operator");
        operatorSpan.TextContent.Should().Contain("compte 30da7398").And.NotContain("30da7398-1111", "le GUID brut complet n'est pas affiché en clair");
        operatorSpan.GetAttribute("title").Should().Be(guid, "le GUID complet reste accessible en infobulle");
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
        bool isArchived = false,
        DocumentContentView? content = null) => new()
    {
        Document = doc ?? Doc("2026-000", "Issued"),
        Events = events ?? [],
        BlockingReason = blockingReason,
        Archive = archive,
        IsArchived = isArchived,
        Content = content ?? DocumentContentView.Empty,
    };

    private static DocumentContentView Content(
        IReadOnlyList<DocumentLineView> lines,
        IReadOnlyList<DocumentChargeView>? charges = null,
        DocumentTotalsCheck? totals = null,
        string? paymentTerms = null,
        IReadOnlyList<DocumentNoteView>? notes = null) => new()
    {
        Lines = lines,
        Charges = charges ?? [],
        Totals = totals ?? Check(),
        PaymentTerms = paymentTerms,
        Notes = notes ?? [],
    };

    private static DocumentNoteView Note(string content, string? subjectCode) => new()
    {
        Content = content,
        SubjectCode = subjectCode,
    };

    private static DocumentLineView Line(
        string label,
        decimal netAmount,
        string category,
        decimal quantity = 1m,
        string sourceRegime = "FR-STD",
        string vatex = "—",
        decimal? taxAmount = null,
        decimal? rate = 20m) => new()
    {
        Label = label,
        Quantity = quantity,
        NetAmount = netAmount,
        SourceRegime = sourceRegime,
        Category = category,
        Vatex = vatex,
        TaxAmount = taxAmount,
        Rate = rate,
    };

    private static DocumentChargeView Charge(string label, bool isCharge, decimal amount) => new()
    {
        Label = label,
        IsCharge = isCharge,
        Amount = amount,
    };

    // Contrôle de cohérence factice pour la vue (le calcul réel est testé dans DocumentLineProjectionTests).
    private static DocumentTotalsCheck Check(bool netConsistent = true, bool taxChecked = true, bool taxConsistent = true) => new()
    {
        ExpectedNet = 1000m,
        DocumentNet = netConsistent ? 1000m : 500m,
        NetConsistent = netConsistent,
        TaxChecked = taxChecked,
        LinesTax = 162.80m,
        DocumentTax = taxConsistent ? 162.80m : 100m,
        TaxConsistent = taxConsistent,
    };

    private static DocumentDto Doc(
        string number,
        string state,
        string customer = "DUPONT J.",
        string? siren = "123456782",
        Guid? id = null,
        bool companyHint = false) => new()
    {
        Id = id ?? Guid.NewGuid(),
        SourceReference = $"src/{number}",
        DocumentNumber = number,
        DocumentType = "invoice",
        IssueDate = new DateOnly(2026, 6, 1),
        SupplierSiren = siren,
        CustomerName = customer,
        CustomerIsCompanyHint = companyHint,
        TotalNet = 1000m,
        TotalTax = 162.80m,
        TotalGross = 1162.80m,
        State = state,
        PayloadHash = "sha256:payload",
        FirstSeenUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
        LastUpdateUtc = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
    };

    private static DocumentEventDto Event(string type, DateTimeOffset when, string? detail = null, string? op = null, string? opName = null) => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = Guid.Empty,
        TimestampUtc = when,
        EventType = type,
        Detail = detail,
        OperatorIdentity = op,
        OperatorName = opName,
    };
}
