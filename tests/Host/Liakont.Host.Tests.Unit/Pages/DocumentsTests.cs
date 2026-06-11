namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Documents;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Pipeline.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Stratum.Common.UI.Components;
using Xunit;

public sealed class DocumentsTests : BunitContext
{
    public DocumentsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();

        // Services réels du design-system (DeclaredListPage : toast, onglets, sélection persistante,
        // registre de templates) via l'extension supportée. Les deux services non couverts (localisation,
        // contexte acteur) sont stubbés.
        Services.AddCommonUI();
        Services.AddSingleton<IStringLocalizer<SharedResources>>(new StubStringLocalizer());
        Services.AddScoped<IActorContextAccessor>(_ => new StubActorContextAccessor());

        // Préférences de grille et filtres enregistrés : persistés en base en production ; no-op en test
        // (la grille retombe sur les colonnes par défaut du registre et une liste de filtres vide).
        Services.AddScoped<IGridPreferenceService>(_ => new NullGridPreferenceService());
        Services.AddScoped<ISavedFilterService>(_ => new NullSavedFilterService());

        // Actions d'envoi (WEB05) : par défaut SANS la permission d'action (lecture seule), service factice.
        // Les tests qui exercent l'envoi RÉ-ENREGISTRENT IPermissionService (canAct: true) et un faux configuré.
        Services.AddScoped<IDocumentSendActions>(_ => new FakeSendActions());

        // Re-vérification (FIX207) : la page injecte IDocumentControlActions quel que soit le rôle ⇒ un faux par défaut.
        Services.AddScoped<IDocumentControlActions>(_ => new FakeControlActions());
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct: false));

        // Mémoire de circuit des filtres (issue #33) : instance fraîche par test (vide par défaut).
        Services.AddScoped<DocumentsListFilterMemory>();
    }

    [Fact]
    public void Should_Render_Filters_And_Counts_And_Mask_Send_Actions_For_Readers()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(
            Doc("2018", "invoice", "Issued"),
            Doc("2019", "invoice", "Blocked"),
            Doc("2020", "credit_note", "Detected")));

        var cut = Render<Documents>();

        // Barre de filtres métier (F10 §2.1).
        cut.FindAll("[data-testid='documents-filters']").Should().ContainSingle();
        cut.FindAll("[data-testid='documents-filter-from']").Should().ContainSingle();
        cut.FindAll("[data-testid='documents-filter-state']").Should().ContainSingle();
        cut.FindAll("[data-testid='documents-filter-type']").Should().ContainSingle();

        // Synthèse par état : 2 factures + 1 avoir → Issued=1, Blocked=1, Detected=1, total 3.
        cut.Find("[data-testid='doc-counts-all']").TextContent.Should().Contain("3");
        cut.Find("[data-testid='doc-counts-Issued']").TextContent.Should().Contain("1");
        cut.Find("[data-testid='doc-counts-Blocked']").TextContent.Should().Contain("1");

        // Sans liakont.actions (lecture seule, défaut du contexte), AUCUNE action d'envoi (WEB05 : masquées).
        cut.FindAll("[data-testid='documents-send-all']").Should().BeEmpty();
        cut.FindAll("[data-testid='documents-trigger-run']").Should().BeEmpty();

        cut.FindAll("[data-testid='documents-error']").Should().BeEmpty();
    }

    [Fact]
    public void An_Operator_Sees_The_Send_Actions_In_The_Toolbar()
    {
        var cut = RenderAsOperator(new FakeSendActions(), Doc("2018", "invoice", "ReadyToSend"));

        cut.FindAll("[data-testid='documents-send-all']").Should().ContainSingle("l'opérateur voit « Tout envoyer »");
        cut.FindAll("[data-testid='documents-trigger-run']").Should().ContainSingle("l'opérateur voit « Lancer un traitement »");
    }

    [Fact]
    public void Tout_Envoyer_Shows_The_Confirmation_With_Count_And_Total_Then_Triggers_The_Send()
    {
        var send = new FakeSendActions
        {
            Summary = new DocumentSendSummary(2, 162.80m),
            SendAllResult = DocumentSendActionResult.Ok("Envoi groupé déclenché : 2 document(s)."),
        };
        var cut = RenderAsOperator(send, Doc("2018", "invoice", "ReadyToSend"));

        cut.Find("[data-testid='documents-send-all']").Click();

        // Confirmation AVANT l'envoi : nombre + montant total + mention irréversible (F10).
        var confirmText = cut.Find("[data-testid='documents-send-all-confirm-text']").TextContent;
        confirmText.Should().Contain("2").And.Contain("162,80").And.Contain("IRRÉVERSIBLE");
        send.SendAllCalls.Should().Be(0, "rien n'est envoyé tant que l'opérateur n'a pas confirmé");

        cut.Find("[data-testid='documents-send-all-confirm-button']").Click();

        send.SendAllCalls.Should().Be(1);
        cut.FindAll("[data-testid='documents-send-all-confirm']").Should().BeEmpty("la confirmation se ferme après l'envoi");
        cut.Find("[data-testid='documents-send-feedback']").TextContent.Should().Contain("Envoi groupé déclenché");
    }

    [Fact]
    public void Tout_Envoyer_Cancel_Does_Not_Trigger_Any_Send()
    {
        var send = new FakeSendActions { Summary = new DocumentSendSummary(2, 162.80m) };
        var cut = RenderAsOperator(send, Doc("2018", "invoice", "ReadyToSend"));

        cut.Find("[data-testid='documents-send-all']").Click();
        cut.Find("[data-testid='documents-send-all-cancel']").Click();

        send.SendAllCalls.Should().Be(0);
        cut.FindAll("[data-testid='documents-send-all-confirm']").Should().BeEmpty();
        cut.FindAll("[data-testid='documents-send-feedback']").Should().BeEmpty("aucune action, aucun retour");
    }

    [Fact]
    public void Lancer_Un_Traitement_Calls_The_Service_And_Shows_Feedback()
    {
        var send = new FakeSendActions { TriggerRunResult = DocumentSendActionResult.Ok("Traitement déclenché.") };
        var cut = RenderAsOperator(send, Doc("2018", "invoice", "ReadyToSend"));

        cut.Find("[data-testid='documents-trigger-run']").Click();

        send.TriggerRunCalls.Should().Be(1);
        cut.Find("[data-testid='documents-send-feedback']").TextContent.Should().Contain("Traitement déclenché");
    }

    [Fact]
    public void Lancer_Un_Traitement_Qui_N_Envoie_Rien_Affiche_Le_Motif_En_Alerte()
    {
        // FIX05 : un run manuel terminé sans rien envoyer remonte le MOTIF (français) en bandeau d'ALERTE —
        // il ne ressemble pas à un succès. Le service renvoie un échec (Success == false) avec le motif.
        var send = new FakeSendActions
        {
            TriggerRunResult = DocumentSendActionResult.Failure(
                "Traitement terminé : aucun document émis. SEND : aucun compte Plateforme Agréée actif pour ce tenant — aucun envoi. Action opérateur : configurez et activez un compte PA (Paramétrage › Plateforme Agréée)."),
        };
        var cut = RenderAsOperator(send, Doc("2018", "invoice", "ReadyToSend"));

        cut.Find("[data-testid='documents-trigger-run']").Click();

        var feedback = cut.Find("[data-testid='documents-send-feedback']");
        feedback.TextContent.Should().Contain("aucun document émis").And.Contain("aucun compte Plateforme Agréée actif");
        feedback.GetAttribute("class").Should().Contain("doc-send-feedback--error", "un run sans envoi n'est pas un succès");
        feedback.GetAttribute("role").Should().Be("alert");
    }

    [Fact]
    public void A_Failed_Send_Surfaces_An_Error_Feedback()
    {
        var send = new FakeSendActions
        {
            Summary = new DocumentSendSummary(1, 100m),
            SendAllResult = DocumentSendActionResult.Failure("L'envoi a échoué : tenant non résolu."),
        };
        var cut = RenderAsOperator(send, Doc("2018", "invoice", "ReadyToSend"));

        cut.Find("[data-testid='documents-send-all']").Click();
        cut.Find("[data-testid='documents-send-all-confirm-button']").Click();

        var feedback = cut.Find("[data-testid='documents-send-feedback']");
        feedback.TextContent.Should().Contain("échoué");
        feedback.GetAttribute("class").Should().Contain("doc-send-feedback--error", "un refus est signalé comme une erreur");
    }

    [Fact]
    public void Tout_Envoyer_Qui_N_Envoie_Rien_Affiche_Le_Motif_En_Alerte()
    {
        // FIX202 : « Tout envoyer » remonte désormais le RÉSULTAT du run (comme « Lancer un traitement »). Un run
        // clôturé sans rien émettre (SIREN non publié) s'affiche en ALERTE avec le motif + l'action corrective —
        // plus de bandeau « déclenché » statique répété en boucle.
        var send = new FakeSendActions
        {
            Summary = new DocumentSendSummary(1, 100m),
            SendAllResult = DocumentSendActionResult.Failure(
                "Le traitement d'envoi du tenant est terminé : aucun document émis. SEND : SIREN non publié auprès de la PA — aucun envoi. Action opérateur : faites publier le SIREN auprès de la PA, puis relancez l'envoi."),
        };
        var cut = RenderAsOperator(send, Doc("2018", "invoice", "ReadyToSend"));

        cut.Find("[data-testid='documents-send-all']").Click();
        cut.Find("[data-testid='documents-send-all-confirm-button']").Click();

        var feedback = cut.Find("[data-testid='documents-send-feedback']");
        feedback.TextContent.Should().Contain("aucun document émis").And.Contain("SIREN non publié");
        feedback.GetAttribute("class").Should().Contain("doc-send-feedback--error", "un envoi sans émission n'est pas un succès");
        feedback.GetAttribute("role").Should().Be("alert");
    }

    [Fact]
    public async Task Envoyer_La_Selection_Confirms_Then_Calls_The_Service_And_Shows_Feedback()
    {
        var send = new FakeSendActions
        {
            Summary = new DocumentSendSummary(2, 162.80m),
            SendSelectionResult = DocumentSendActionResult.Ok("Envoi déclenché : le traitement d'envoi du tenant émet TOUS les documents prêts."),
        };
        var selected = Doc("2018", "invoice", "ReadyToSend");
        var cut = RenderAsOperator(send, selected, Doc("2019", "invoice", "ReadyToSend"));

        // « Envoyer la sélection » est la barre d'actions groupées de DeclaredListPage : son rappel Execute EST
        // le câblage WEB05 (ouvre la confirmation, puis envoi APRÈS confirmation explicite). On l'invoque
        // directement (la sélection réelle d'une ligne de la grille Radzen dépend d'un JS interop indisponible en
        // bUnit), ce qui exerce de façon DÉTERMINISTE Documents → confirmation → IDocumentSendActions → bandeau.
        var listPage = cut.FindComponent<DeclaredListPage<DocumentSummaryDto>>();
        var action = listPage.Instance.BulkActions!.Single(a => a.Id == "send-selection");
        action.SuppressSuccessToast.Should().BeTrue("l'action ouvre une confirmation : pas de toast « traité(s) » trompeur avant l'envoi");

        await cut.InvokeAsync(() => action.Execute!(new[] { selected }));

        // L'envoi N'EST PAS encore déclenché : seule la confirmation s'affiche, avec le périmètre RÉEL (tout le
        // tenant, ADR-0016), pas seulement la sélection — et la mention irréversible.
        send.LastSelection.Should().BeNull("rien n'est envoyé tant que l'opérateur n'a pas confirmé");
        cut.Find("[data-testid='documents-send-all-confirm-text']").TextContent
            .Should().Contain("TOUS les documents prêts").And.Contain("IRRÉVERSIBLE");

        cut.Find("[data-testid='documents-send-all-confirm-button']").Click();

        send.LastSelection.Should().ContainSingle().Which.Should().Be(selected.Id, "le service reçoit l'identifiant du document sélectionné");
        cut.Find("[data-testid='documents-send-feedback']").TextContent.Should().Contain("Envoi déclenché");
    }

    [Fact]
    public async Task Envoyer_La_Selection_Qui_N_Envoie_Rien_Affiche_Le_Motif_En_Alerte()
    {
        // FIX202 : « Envoyer la sélection » remonte désormais le RÉSULTAT du run (comme « Tout envoyer » et « Lancer
        // un traitement »). Un run clôturé sans rien émettre s'affiche en ALERTE avec le motif — le chemin d'échec UI
        // PROPRE À LA SÉLECTION est asservi (le rendu d'échec est asserté par mode : role="alert" + --error).
        var send = new FakeSendActions
        {
            Summary = new DocumentSendSummary(1, 100m),
            SendSelectionResult = DocumentSendActionResult.Failure(
                "Le traitement d'envoi du tenant est terminé : aucun document émis. SEND : SIREN non publié auprès de la PA — aucun envoi. Action opérateur : faites publier le SIREN auprès de la PA, puis relancez l'envoi."),
        };
        var selected = Doc("2018", "invoice", "ReadyToSend");
        var cut = RenderAsOperator(send, selected);

        var listPage = cut.FindComponent<DeclaredListPage<DocumentSummaryDto>>();
        var action = listPage.Instance.BulkActions!.Single(a => a.Id == "send-selection");
        await cut.InvokeAsync(() => action.Execute!(new[] { selected }));
        cut.Find("[data-testid='documents-send-all-confirm-button']").Click();

        var feedback = cut.Find("[data-testid='documents-send-feedback']");
        feedback.TextContent.Should().Contain("aucun document émis").And.Contain("SIREN non publié");
        feedback.GetAttribute("class").Should().Contain("doc-send-feedback--error", "un envoi sans émission n'est pas un succès");
        feedback.GetAttribute("role").Should().Be("alert");
    }

    [Fact]
    public void An_Operator_Sees_Reverifier_Tout_As_A_Global_Toolbar_Action_Not_In_The_Selection_Bar()
    {
        // FIX302 : « Revérifier tout » est désormais une action GLOBALE de la barre d'outils (haut à droite), PAS une
        // action groupée. Sans sélection et sans action groupée globale, la barre de sélection ne s'affiche pas du tout.
        var cut = RenderAsOperator(new FakeSendActions(), Doc("2018", "invoice", "Blocked"));

        cut.FindAll("[data-testid='documents-recheck-all']").Should().ContainSingle("« Revérifier tout » est dans la barre d'outils");
        cut.FindAll("[data-testid='documents-bulk-bar']").Should().BeEmpty("aucune barre de sélection sans sélection (plus d'action groupée globale)");
        cut.FindAll("[data-testid='documents-bulk-recheck-all']").Should().BeEmpty("« Revérifier tout » n'est plus une action groupée");
        cut.FindAll("[data-testid='documents-bulk-recheck-selection']").Should().BeEmpty("action sélection-scopée masquée sans sélection");
    }

    [Fact]
    public void Reverifier_Tout_Is_Disabled_When_No_Blocked_Document_In_Scope()
    {
        // FIX302 : sur un périmètre sans aucun document bloqué, l'action globale est rendue mais DÉSACTIVÉE — plus
        // jamais de bouton orphelin/actif sur une liste vide ou sans bloqué.
        var cut = RenderAsOperator(new FakeSendActions(), Doc("2018", "invoice", "Issued"));

        var recheckAll = cut.FindComponents<StratumButton>().Single(b => b.Instance.TestId == "documents-recheck-all");
        recheckAll.Instance.Disabled.Should().BeTrue("aucun document bloqué dans le périmètre : rien à revérifier");
    }

    [Fact]
    public void Reverifier_Tout_Is_Enabled_When_A_Blocked_Document_Is_In_Scope()
    {
        // Contre-épreuve : dès qu'un bloqué est dans le périmètre, l'action est active.
        var cut = RenderAsOperator(new FakeSendActions(), Doc("2018", "invoice", "Blocked"));

        var recheckAll = cut.FindComponents<StratumButton>().Single(b => b.Instance.TestId == "documents-recheck-all");
        recheckAll.Instance.Disabled.Should().BeFalse("un document bloqué est dans le périmètre : action disponible");
    }

    [Fact]
    public void The_Documents_List_Opts_Out_Of_The_Persistent_Selection_Bar()
    {
        // FIX302 : la page Documents pilote ses actions groupées sur la sélection de la page courante uniquement ;
        // elle désactive la barre de sélection persistante Stratum, dont le compteur « (0 au total) » était incohérent
        // (deux barres concurrentes). Une SEULE barre de sélection subsiste, celle des actions groupées.
        var cut = RenderAsOperator(new FakeSendActions(), Doc("2018", "invoice", "Blocked"));
        var listPage = cut.FindComponent<DeclaredListPage<DocumentSummaryDto>>();

        listPage.Instance.EnablePersistentSelection.Should().BeFalse("compteur cohérent : pas de barre persistante « (0 au total) »");
    }

    [Fact]
    public void Readers_See_No_Selection_Bar_And_No_Recheck_Actions()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(Doc("2019", "invoice", "Blocked")));

        var cut = Render<Documents>();

        // Sans liakont.actions : BulkActions null ⇒ aucune barre de sélection ; aucune action de re-vérification,
        // ni dans la barre de sélection, ni dans la barre d'outils (action globale masquée pour le lecteur).
        cut.FindAll("[data-testid='documents-bulk-bar']").Should().BeEmpty();
        cut.FindAll("[data-testid='documents-bulk-recheck-all']").Should().BeEmpty();
        cut.FindAll("[data-testid='documents-recheck-all']").Should().BeEmpty("action globale de re-vérification masquée sans liakont.actions");
    }

    [Fact]
    public void Reverifier_Tout_And_La_Selection_Are_Declared_With_The_Right_Scope()
    {
        var cut = RenderAsOperator(new FakeSendActions(), Doc("2018", "invoice", "Blocked"));
        var listPage = cut.FindComponent<DeclaredListPage<DocumentSummaryDto>>();

        // FIX302 : « Revérifier tout » N'EST PLUS une action groupée (passée en action globale de barre d'outils).
        listPage.Instance.BulkActions!.Should().NotContain(a => a.Id == "recheck-all", "« Revérifier tout » est une action globale, pas groupée");

        var selection = listPage.Instance.BulkActions!.Single(a => a.Id == "recheck-selection");
        selection.RequiresSelection.Should().BeTrue("« Revérifier la sélection » est sélection-scopée");
        selection.SuppressSuccessToast.Should().BeTrue("le retour réel passe par le bandeau de compteurs, pas un toast");
    }

    [Fact]
    public void Reverifier_Tout_Rechecks_All_Blocked_In_Scope_And_Shows_Counters()
    {
        var control = new FakeControlActions
        {
            BulkResult = DocumentBulkRecheckResult.From(
                new DocumentBulkRecheckSummary { Total = 2, Unblocked = 1, StillBlocked = 1, Unavailable = 0, Skipped = 0 }),
        };
        var blocked1 = Doc("2018", "invoice", "Blocked");
        var blocked2 = Doc("2019", "invoice", "Blocked");
        var issued = Doc("2020", "invoice", "Issued");
        var cut = RenderAsOperator(new FakeSendActions(), control, blocked1, blocked2, issued);

        // L'action globale de la barre d'outils opère sur le périmètre de la page (tous les bloqués chargés).
        cut.Find("[data-testid='documents-recheck-all']").Click();

        // Seuls les BLOQUÉS du périmètre sont re-vérifiés (l'émis est exclu).
        control.LastRecheckedIds.Should().BeEquivalentTo(new[] { blocked1.Id, blocked2.Id });
        cut.Find("[data-testid='documents-recheck-feedback']").TextContent
            .Should().Contain("1 débloqué").And.Contain("1 resté bloqué");
    }

    [Fact]
    public async Task Reverifier_La_Selection_Rechecks_Only_The_Blocked_Selected_Documents()
    {
        var control = new FakeControlActions
        {
            BulkResult = DocumentBulkRecheckResult.From(
                new DocumentBulkRecheckSummary { Total = 1, Unblocked = 1, StillBlocked = 0, Unavailable = 0, Skipped = 0 }),
        };
        var blocked = Doc("2018", "invoice", "Blocked");
        var issued = Doc("2019", "invoice", "Issued");
        var cut = RenderAsOperator(new FakeSendActions(), control, blocked, issued);

        var listPage = cut.FindComponent<DeclaredListPage<DocumentSummaryDto>>();
        var action = listPage.Instance.BulkActions!.Single(a => a.Id == "recheck-selection");

        // La sélection contient un bloqué + un émis ; seul le bloqué part au recheck (le serveur re-valide ensuite).
        await cut.InvokeAsync(() => action.Execute!(new[] { blocked, issued }));

        control.LastRecheckedIds.Should().ContainSingle().Which.Should().Be(blocked.Id);
        cut.Find("[data-testid='documents-recheck-feedback']").TextContent.Should().Contain("1 débloqué");
    }

    [Fact]
    public async Task Reverifier_La_Selection_Sans_Document_Bloque_Affiche_Un_Message_Sans_Appeler_Le_Service()
    {
        var control = new FakeControlActions();
        var issued = Doc("2019", "invoice", "Issued");
        var cut = RenderAsOperator(new FakeSendActions(), control, issued);

        var listPage = cut.FindComponent<DeclaredListPage<DocumentSummaryDto>>();
        var action = listPage.Instance.BulkActions!.Single(a => a.Id == "recheck-selection");
        await cut.InvokeAsync(() => action.Execute!(new[] { issued }));

        control.LastRecheckedIds.Should().BeNull("aucun document bloqué : le service n'est pas appelé");
        cut.Find("[data-testid='documents-recheck-feedback']").TextContent
            .Should().Contain("Aucun document bloqué dans la sélection");
    }

    private IRenderedComponent<Documents> RenderAsOperator(FakeSendActions send, params DocumentSummaryDto[] docs) =>
        RenderAsOperator(send, new FakeControlActions(), docs);

    private IRenderedComponent<Documents> RenderAsOperator(FakeSendActions send, FakeControlActions control, params DocumentSummaryDto[] docs)
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(docs));
        Services.AddScoped<IDocumentSendActions>(_ => send);
        Services.AddScoped<IDocumentControlActions>(_ => control);
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct: true));
        return Render<Documents>();
    }

    [Fact]
    public void Should_Render_Document_Rows_With_State_Badge()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(
            Doc("2018", "invoice", "Issued")));

        var cut = Render<Documents>();

        // La ligne a traversé LoadItems → la grille → le ColumnTemplate d'état (badge FR).
        cut.FindAll("[data-testid='doc-state-Issued']").Should().NotBeEmpty();
    }

    [Fact]
    public void Should_Show_Error_Banner_When_Load_Throws()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Throwing());

        var cut = Render<Documents>();

        // L'échec de chargement reste VISIBLE (bandeau) et n'expose pas la liste (anti faux-vert).
        cut.FindAll("[data-testid='documents-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='documents-filters']").Should().BeEmpty();
    }

    [Fact]
    public void Selecting_A_State_Should_Filter_The_Rendered_Rows()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(
            Doc("2018", "invoice", "Issued", customer: "ALICE"),
            Doc("2019", "invoice", "Blocked", customer: "BOBBY")));

        var cut = Render<Documents>();
        cut.Markup.Should().Contain("ALICE").And.Contain("BOBBY");

        cut.Find("[data-testid='documents-filter-state']").Change("Blocked");

        // Seule la ligne Bloqué reste dans la grille (le filtre client de DeclaredListPage s'applique).
        cut.Markup.Should().Contain("BOBBY");
        cut.Markup.Should().NotContain("ALICE");
    }

    [Fact]
    public void Selecting_A_Type_Should_Update_The_Counts_And_The_Rows()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(
            Doc("2018", "invoice", "Issued", customer: "ALICE"),
            Doc("2020", "credit_note", "Blocked", customer: "CAROLE")));

        var cut = Render<Documents>();

        cut.Find("[data-testid='documents-filter-type']").Change("Avoir");

        // Les compteurs honorent le type : seul l'avoir (Bloqué) est compté.
        cut.Find("[data-testid='doc-counts-Blocked']").TextContent.Should().Contain("1");
        cut.Find("[data-testid='doc-counts-Issued']").TextContent.Should().Contain("0");
        cut.Find("[data-testid='doc-counts-all']").TextContent.Should().Contain("1");

        // Les lignes sont filtrées : seule la facture (ALICE) disparaît.
        cut.Markup.Should().Contain("CAROLE");
        cut.Markup.Should().NotContain("ALICE");
    }

    [Fact]
    public void Clicking_A_Count_Chip_Should_Filter_The_List_By_That_State()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(
            Doc("2018", "invoice", "Issued", customer: "ALICE"),
            Doc("2019", "invoice", "Blocked", customer: "BOBBY")));

        var cut = Render<Documents>();

        cut.Find("[data-testid='doc-counts-Blocked']").Click();

        cut.Markup.Should().Contain("BOBBY");
        cut.Markup.Should().NotContain("ALICE");
    }

    // ── Persistance des filtres (issue GitHub #33) ── Les filtres survivent à l'aller-retour vers la
    // fiche détail : publiés dans l'URL (lien partageable, bouton Précédent) ET dans la mémoire de
    // circuit (le « Retour à la liste » de la fiche est un lien statique /documents sans query).
    [Fact]
    public void Changing_A_Filter_Should_Publish_It_To_The_Url_And_The_Circuit_Memory()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(
            Doc("2018", "invoice", "Issued"),
            Doc("2019", "invoice", "Blocked")));

        var cut = Render<Documents>();
        cut.Find("[data-testid='documents-filter-state']").Change("Blocked");

        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.Uri.Should().Contain("etat=Blocked", "le filtre État est publié dans l'URL (lien partageable, retour navigateur)");
        nav.Uri.Should().Contain("du=", "la période est publiée dans l'URL");

        var memory = Services.GetRequiredService<DocumentsListFilterMemory>();
        memory.State.Should().Be("Blocked", "le filtre État est mémorisé pour le « Retour à la liste » de la fiche");
        memory.From.Should().NotBeNull();
    }

    [Fact]
    public void Rendering_With_Filter_Query_Parameters_Should_Restore_The_Filters()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(
            Doc("2018", "invoice", "Issued", customer: "ALICE"),
            Doc("2019", "invoice", "Blocked", customer: "BOBBY")));

        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo("/documents?du=2026-06-01&au=2026-06-30&etat=Blocked");

        var cut = Render<Documents>();

        cut.Find("[data-testid='documents-filter-state']").GetAttribute("value").Should().Be("Blocked");
        cut.Markup.Should().Contain("BOBBY");
        cut.Markup.Should().NotContain("ALICE", "le filtre État restauré depuis l'URL s'applique dès le premier rendu");

        // L'URL alimente AUSSI la mémoire de circuit : après l'ouverture d'un lien partagé, le
        // « Retour à la liste » de la fiche (lien statique /documents) retrouve les mêmes filtres.
        var memory = Services.GetRequiredService<DocumentsListFilterMemory>();
        memory.State.Should().Be("Blocked");
        memory.From.Should().Be(new DateOnly(2026, 6, 1));
    }

    [Fact]
    public void Rendering_With_An_Unknown_State_In_The_Url_Should_Fall_Back_To_All()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(
            Doc("2018", "invoice", "Issued", customer: "ALICE")));

        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo("/documents?etat=NImporteQuoi");

        var cut = Render<Documents>();

        // URL retouchée à la main : la clé d'état inconnue est ignorée, jamais d'erreur.
        cut.Find("[data-testid='documents-filter-state']").GetAttribute("value").Should().BeNullOrEmpty();
        cut.Markup.Should().Contain("ALICE");
    }

    [Fact]
    public void Rendering_After_A_Detail_Round_Trip_Should_Restore_The_Memorized_Filters()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(
            Doc("2018", "invoice", "Issued", customer: "ALICE"),
            Doc("2019", "invoice", "Blocked", customer: "BOBBY")));

        // Simule le retour depuis la fiche détail dans le MÊME circuit : la mémoire contient les
        // derniers filtres, l'URL (/documents, lien statique du bouton « Retour à la liste ») est nue.
        var memory = Services.GetRequiredService<DocumentsListFilterMemory>();
        memory.Remember(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "Blocked", typeLabel: null);

        var cut = Render<Documents>();

        cut.Find("[data-testid='documents-filter-state']").GetAttribute("value").Should().Be("Blocked");
        cut.Markup.Should().Contain("BOBBY");
        cut.Markup.Should().NotContain("ALICE", "les filtres mémorisés sont restaurés au retour de la fiche");
        cut.Find("[data-testid='documents-filter-from']").GetAttribute("value").Should().Be("2026-06-01");
    }

    [Fact]
    public void Changing_Period_To_A_Scope_Without_The_Selected_Type_Should_Reset_It()
    {
        // 1er périmètre : une facture (type « Facture » disponible). 2e périmètre : un avoir seulement.
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Switching(
            [Doc("2018", "invoice", "Issued", customer: "ALICE")],
            [Doc("2030", "credit_note", "Blocked", customer: "CAROLE")]));

        var cut = Render<Documents>();
        cut.Find("[data-testid='documents-filter-type']").Change("Facture");

        // Changement de période → rechargement d'un périmètre où « Facture » n'existe plus → retour à Tous.
        cut.Find("[data-testid='documents-filter-from']").Change("2026-05-01");

        var typeSelect = cut.Find("[data-testid='documents-filter-type']");
        typeSelect.GetAttribute("value").Should().BeNullOrEmpty("le type sélectionné disparu est réinitialisé à « Tous »");
        cut.Markup.Should().NotContain(">Facture<", "l'option Facture n'existe plus dans ce périmètre");
        cut.Markup.Should().Contain("CAROLE");
    }

    private static DocumentSummaryDto Doc(string number, string type, string state, string customer = "DUPONT J.") => new()
    {
        Id = Guid.NewGuid(),
        DocumentNumber = number,
        DocumentType = type,
        IssueDate = new DateOnly(2026, 6, 1),
        CustomerName = customer,
        TotalGross = 1162.80m,
        State = state,
        LastUpdateUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private sealed class FakeDocumentConsoleQueries : IDocumentConsoleQueries
    {
        private readonly IReadOnlyList<IReadOnlyList<DocumentSummaryDto>>? _scopes;
        private readonly bool _throws;
        private int _call;

        private FakeDocumentConsoleQueries(IReadOnlyList<IReadOnlyList<DocumentSummaryDto>>? scopes, bool throws)
        {
            _scopes = scopes;
            _throws = throws;
        }

        public static FakeDocumentConsoleQueries Returning(params DocumentSummaryDto[] documents) => new([documents], throws: false);

        // Renvoie un périmètre différent à chaque rechargement (1er appel = premier jeu, etc. ; le dernier
        // se répète) — pour tester le changement de période.
        public static FakeDocumentConsoleQueries Switching(params DocumentSummaryDto[][] scopes) => new(scopes, throws: false);

        public static FakeDocumentConsoleQueries Throwing() => new(null, throws: true);

        public Task<IReadOnlyList<DocumentSummaryDto>> GetDocumentsInPeriodAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
        {
            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé de chargement des documents.");
            }

            var index = Math.Min(_call, _scopes!.Count - 1);
            _call++;
            return Task.FromResult(_scopes[index]);
        }
    }

    private sealed class FakeSendActions : IDocumentSendActions
    {
        public DocumentSendSummary Summary { get; set; } = new(0, 0m);

        public DocumentSendActionResult SendAllResult { get; set; } = DocumentSendActionResult.Ok("Envoi groupé déclenché.");

        public DocumentSendActionResult TriggerRunResult { get; set; } = DocumentSendActionResult.Ok("Traitement déclenché.");

        public DocumentSendActionResult SendSelectionResult { get; set; } = DocumentSendActionResult.Ok("Envoi de la sélection déclenché.");

        public int SendAllCalls { get; private set; }

        public int TriggerRunCalls { get; private set; }

        public IReadOnlyList<Guid>? LastSelection { get; private set; }

        public Task<DocumentSendActionResult> SendSelectionAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken = default)
        {
            LastSelection = documentIds.ToList();
            return Task.FromResult(SendSelectionResult);
        }

        public Task<DocumentSendSummary> SummarizeReadyToSendAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Summary);

        public Task<DocumentSendActionResult> SendAllAsync(CancellationToken cancellationToken = default)
        {
            SendAllCalls++;
            return Task.FromResult(SendAllResult);
        }

        public Task<DocumentSendActionResult> TriggerRunAsync(CancellationToken cancellationToken = default)
        {
            TriggerRunCalls++;
            return Task.FromResult(TriggerRunResult);
        }
    }

    private sealed class FakeControlActions : IDocumentControlActions
    {
        public DocumentBulkRecheckResult BulkResult { get; set; } = DocumentBulkRecheckResult.From(
            new DocumentBulkRecheckSummary { Total = 0, Unblocked = 0, StillBlocked = 0, Unavailable = 0, Skipped = 0 });

        public IReadOnlyList<Guid>? LastRecheckedIds { get; private set; }

        public Task<DocumentControlActionResult> SubmitVerdictAsync(Guid documentId, ConsoleVerdict verdict, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentControlActionResult.Ok("ok", "Blocked"));

        public Task<DocumentControlActionResult> RecheckAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentControlActionResult.Ok("ok", "ReadyToSend"));

        public Task<DocumentBulkRecheckResult> RecheckManyAsync(IReadOnlyList<Guid> documentIds, CancellationToken cancellationToken = default)
        {
            LastRecheckedIds = documentIds.ToList();
            return Task.FromResult(BulkResult);
        }
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

    private sealed class NullSavedFilterService : ISavedFilterService
    {
        public Task<IReadOnlyList<SavedFilter>> ListAsync(Guid userId, string gridKey, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SavedFilter>>([]);

        public Task<SavedFilter?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<SavedFilter?>(null);

        public Task<SavedFilter> SaveAsync(SavedFilter filter, CancellationToken ct = default) =>
            Task.FromResult(filter);

        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;

        public Task SetDefaultAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullGridPreferenceService : IGridPreferenceService
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

    private sealed class StubStringLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(System.Globalization.CultureInfo.InvariantCulture, name, arguments), resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class StubActorContextAccessor : IActorContextAccessor
    {
        public IActorContext Current { get; } = new StubActorContext();

        private sealed class StubActorContext : IActorContext
        {
            public Guid UserId => Guid.Empty;

            public Guid CorrelationId => Guid.Empty;

            public bool IsAuthenticated => true;

            public string? DisplayName => "Test";

            public string? Email => null;

            public Guid? CompanyId => null;

            public string? Timezone => null;

            public string? Language => "fr";

            public string? TenantId => "tenant-test";
        }
    }
}
