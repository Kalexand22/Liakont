namespace Liakont.Host.Tests.Unit.Components;

using System;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.Documents;
using Liakont.Modules.Documents.Contracts.DTOs;
using Microsoft.AspNetCore.Components;
using Xunit;

// DocumentActionBar (FIX04b) — barre d'actions PERMANENTE en tête de fiche détail document. Composant pur :
// les actions proposées dépendent de l'état du document et de la permission liakont.actions (CanAct), et
// chaque action est un EventCallback orchestré par la page. Ces tests couvrent la visibilité par état +
// permission (acceptance FIX04b : « actions selon état + permission, masquées sans liakont.actions ») et le
// câblage des callbacks (garde anti-inversion B2C/B2B).
public sealed class DocumentActionBarTests : BunitContext
{
    public DocumentActionBarTests()
    {
        // StratumButton (RadzenButton) peut appeler du JS : mode permissif.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Should_Show_Verdict_And_Recheck_When_CanAct_And_Blocked_With_Company_Hint()
    {
        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("2026-010", "Blocked", companyHint: true)))
            .Add(b => b.CanAct, true));

        cut.FindAll("[data-testid='document-detail-action-bar']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-verdict-hint']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-verdict-b2c']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-verdict-b2b']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-recheck']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-send']").Should().BeEmpty("un document bloqué n'est pas envoyable");

        // Le hint d'envoi ADR-0016 est absent sur un document bloqué (il n'est pas ReadyToSend).
        cut.FindAll("[data-testid='document-detail-send-hint']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Hide_Everything_When_Not_CanAct_Even_If_Blocked()
    {
        // Sans la permission d'action, AUCUN bouton — la fiche reste consultable en lecture (WEB03a).
        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("2026-011", "Blocked", companyHint: true)))
            .Add(b => b.CanAct, false));

        cut.FindAll("[data-testid='document-detail-action-region']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-action-bar']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-verdict-b2c']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-recheck']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Hide_Verdict_But_Keep_Recheck_When_No_Company_Hint()
    {
        // Aucun indice « société » : le verdict B2B/B2C n'est pas proposé, mais la re-vérification reste offerte.
        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("2026-012", "Blocked", companyHint: false)))
            .Add(b => b.CanAct, true));

        cut.FindAll("[data-testid='document-detail-verdict-hint']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-verdict-b2c']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-verdict-b2b']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-recheck']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Hide_Verdict_When_Already_Confirmed_B2c_But_Keep_Recheck()
    {
        // Verdict déjà posé : on ne le re-propose pas (mais on peut encore re-vérifier pour débloquer).
        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("2026-013", "Blocked", companyHint: true, confirmedB2c: true)))
            .Add(b => b.CanAct, true));

        cut.FindAll("[data-testid='document-detail-verdict-b2c']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-recheck']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Offer_Send_When_ReadyToSend()
    {
        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("2026-014", "ReadyToSend", companyHint: true)))
            .Add(b => b.CanAct, true));

        cut.FindAll("[data-testid='document-detail-action-bar']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-send']").Should().ContainSingle();

        // Le hint ADR-0016 avertit que l'envoi émet TOUS les ReadyToSend du tenant.
        cut.FindAll("[data-testid='document-detail-send-hint']").Should().ContainSingle();

        // Sur un document prêt à l'envoi, ni verdict ni re-vérification (réservés au blocage).
        cut.FindAll("[data-testid='document-detail-verdict-b2c']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-recheck']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Disable_Send_With_The_Suspension_Reason_When_Sends_Are_Suspended()
    {
        // Table TVA non validée : même affordance que le « Tout envoyer » de la liste (lot 2) —
        // bouton désactivé + motif visible (hint) ; la garde réelle reste serveur.
        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("2026-016", "ReadyToSend")))
            .Add(b => b.CanAct, true)
            .Add(b => b.SendsSuspended, true));

        var send = cut.Find("[data-testid='document-detail-send']");
        send.HasAttribute("disabled").Should().BeTrue("les envois sont suspendus tant que la table TVA n'est pas validée");
        cut.Find("[data-testid='document-detail-send-hint']").TextContent
            .Should().Contain("suspendus").And.Contain("table TVA");
    }

    [Fact]
    public void Should_Hide_Send_For_An_E_Reported_Document()
    {
        // BUG-24/ADR-0037 : un document e-reporté porte l'état persisté EReported (≠ ReadyToSend) → « Envoyer » n'a
        // pas de raison d'être (voie agrégée, pas voie document) et, aucune autre action n'étant pertinente, la barre
        // entière disparaît (l'opérateur retrouve la déclaration depuis la liste des émissions e-reporting B2C).
        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("9000004", "EReported", companyHint: true)))
            .Add(b => b.CanAct, true));

        cut.FindAll("[data-testid='document-detail-send']").Should().BeEmpty("un document e-reporté ne s'envoie pas par la voie document");
        cut.FindAll("[data-testid='document-detail-send-hint']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-action-region']").Should().BeEmpty("aucune action pertinente → pas de barre");
    }

    [Fact]
    public void Should_Hide_Send_For_A_Residual_ReadyToSend_Document_Already_E_Reported()
    {
        // GDF03 (filet read-time léger) : un document déjà DÉCLARÉ par la voie AGRÉGÉE (journal d'émission Issued)
        // mais resté ReadyToSend dans la fenêtre RÉSIDUELLE transitoire (avant rattrapage GDF02) porte un lot
        // d'émission résolu (EReportedBatchId != null). « Envoyer » ne doit PAS réapparaître de façon trompeuse
        // (la garde réelle reste serveur : SendTenantJob défère toute déclaration 10.3). Aucune autre action
        // n'étant pertinente, la barre entière disparaît.
        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("9000005", "ReadyToSend", companyHint: true), eReportedBatchId: Guid.NewGuid()))
            .Add(b => b.CanAct, true));

        cut.FindAll("[data-testid='document-detail-send']").Should().BeEmpty("un document déjà e-reporté (résidu ReadyToSend) ne se renvoie pas par la voie document");
        cut.FindAll("[data-testid='document-detail-send-hint']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-action-region']").Should().BeEmpty("aucune action pertinente → pas de barre");
    }

    [Fact]
    public void Should_Still_Offer_Send_For_A_ReadyToSend_Document_Never_E_Reported()
    {
        // Garde-fou anti-régression : un ReadyToSend ORDINAIRE (jamais e-reporté → aucun lot résolu) reste envoyable.
        // Le masquage GDF03 ne se déclenche QUE sur la présence d'un lot d'émission (signal « déjà déclaré »).
        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("9000006", "ReadyToSend"), eReportedBatchId: null))
            .Add(b => b.CanAct, true));

        cut.FindAll("[data-testid='document-detail-send']").Should().ContainSingle("un document prêt à l'envoi jamais déclaré reste envoyable");
    }

    [Fact]
    public void Should_Hide_Send_When_Not_CanAct_Even_If_ReadyToSend()
    {
        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("2026-015", "ReadyToSend")))
            .Add(b => b.CanAct, false));

        cut.FindAll("[data-testid='document-detail-action-region']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-send']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Recheck_For_A_RejectedByPa_Document_With_Permission()
    {
        // Un document rejeté par la PA n'est plus un cul-de-sac : la re-vérification est offerte (remet en envoi si
        // la cause est corrigée, sinon bloque avec le motif). Libellé explicite, pas de verdict ni d'envoi.
        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("2026-030", "RejectedByPa", companyHint: true)))
            .Add(b => b.CanAct, true));

        cut.FindAll("[data-testid='document-detail-action-bar']").Should().ContainSingle();
        var recheck = cut.Find("[data-testid='document-detail-recheck']");
        recheck.TextContent.Should().Contain("Re-vérifier et remettre en envoi");

        // Sur un rejeté : ni verdict garde-fou (réservé à Blocked) ni envoi (réservé à ReadyToSend).
        cut.FindAll("[data-testid='document-detail-verdict-b2c']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-verdict-hint']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-send']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Hide_Recheck_For_A_RejectedByPa_Document_Without_Permission()
    {
        // Sans la permission d'action, AUCUN bouton même pour un rejeté — la fiche reste consultable en lecture.
        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("2026-031", "RejectedByPa", companyHint: true)))
            .Add(b => b.CanAct, false));

        cut.FindAll("[data-testid='document-detail-action-region']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-recheck']").Should().BeEmpty();
    }

    [Fact]
    public void Recheck_Button_On_A_RejectedByPa_Document_Invokes_OnRecheck()
    {
        var rechecked = false;

        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("2026-032", "RejectedByPa")))
            .Add(b => b.CanAct, true)
            .Add(b => b.OnRecheck, EventCallback.Factory.Create(this, () => rechecked = true)));

        cut.Find("[data-testid='document-detail-recheck']").Click();

        rechecked.Should().BeTrue("le bouton de re-vérification d'un rejeté déclenche OnRecheck (même câblage que Blocked)");
    }

    [Fact]
    public void Should_Render_No_Action_Bar_When_State_Has_No_Applicable_Action()
    {
        // Document émis (ni bloqué, ni prêt à l'envoi) : aucune action pertinente → pas de barre du tout.
        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("2026-016", "Issued", companyHint: true)))
            .Add(b => b.CanAct, true));

        cut.FindAll("[data-testid='document-detail-action-region']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-action-bar']").Should().BeEmpty();
    }

    [Fact]
    public void Verdict_And_Recheck_Buttons_Map_To_The_Correct_Callbacks()
    {
        // Garde anti-inversion : B2C → OnConfirmB2c, B2B → OnHandleManually, re-vérification → OnRecheck.
        var confirmed = false;
        var manual = false;
        var rechecked = false;

        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("2026-017", "Blocked", companyHint: true)))
            .Add(b => b.CanAct, true)
            .Add(b => b.OnConfirmB2c, EventCallback.Factory.Create(this, () => confirmed = true))
            .Add(b => b.OnHandleManually, EventCallback.Factory.Create(this, () => manual = true))
            .Add(b => b.OnRecheck, EventCallback.Factory.Create(this, () => rechecked = true)));

        cut.Find("[data-testid='document-detail-verdict-b2c']").Click();
        cut.Find("[data-testid='document-detail-verdict-b2b']").Click();
        cut.Find("[data-testid='document-detail-recheck']").Click();

        confirmed.Should().BeTrue("le bouton « Confirmer particulier (B2C) » déclenche OnConfirmB2c");
        manual.Should().BeTrue("le bouton « Traiter manuellement (B2B) » déclenche OnHandleManually");
        rechecked.Should().BeTrue("le bouton « Revérifier maintenant » déclenche OnRecheck");
    }

    [Fact]
    public void Send_Button_Invokes_OnSend()
    {
        var sent = false;

        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("2026-018", "ReadyToSend")))
            .Add(b => b.CanAct, true)
            .Add(b => b.OnSend, EventCallback.Factory.Create(this, () => sent = true)));

        cut.Find("[data-testid='document-detail-send']").Click();

        sent.Should().BeTrue("le bouton « Envoyer » déclenche OnSend");
    }

    [Fact]
    public void Buttons_Are_Disabled_While_An_Action_Is_In_Progress()
    {
        var cut = Render<DocumentActionBar>(p => p
            .Add(b => b.Model, Model(Doc("2026-019", "Blocked", companyHint: true)))
            .Add(b => b.CanAct, true)
            .Add(b => b.ActionInProgress, true));

        cut.Find("[data-testid='document-detail-verdict-b2c']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='document-detail-verdict-b2b']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='document-detail-recheck']").HasAttribute("disabled").Should().BeTrue();
    }

    private static DocumentDetailViewModel Model(DocumentDto doc, Guid? eReportedBatchId = null) => new()
    {
        Document = doc,
        Events = [],
        BlockingReason = string.Equals(doc.State, "Blocked", StringComparison.Ordinal) ? "Un contrôle a échoué." : null,
        Archive = null,
        IsArchived = false,
        EReportedBatchId = eReportedBatchId,
    };

    private static DocumentDto Doc(
        string number,
        string state,
        bool companyHint = false,
        bool confirmedB2c = false) => new()
    {
        Id = Guid.NewGuid(),
        SourceReference = $"src/{number}",
        DocumentNumber = number,
        DocumentType = "invoice",
        IssueDate = new DateOnly(2026, 6, 1),
        SupplierSiren = "123456782",
        CustomerName = "ACME SARL",
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
}
