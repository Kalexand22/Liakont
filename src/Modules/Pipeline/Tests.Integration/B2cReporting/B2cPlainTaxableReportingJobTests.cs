namespace Liakont.Modules.Pipeline.Tests.Integration.B2cReporting;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Tests.Integration.Check;
using Liakont.Modules.Pipeline.Tests.Integration.Send;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Fake;
using Xunit;

/// <summary>
/// #7 — JOB e-reporting B2C des documents ORDINAIRES taxables (flux 10.3, hors enchères, F03 §2.9) sur base réelle
/// (Testcontainers) : découverte des déclarations ordinaires prêtes (acheteur particulier, lignes taxables, AUCUN
/// frais) → agrégation jour×devise×taux PAR catégorie → transmission PA (TLB1 facture client / TPS1 note
/// d'honoraires, SE) → journal d'émission attempt-once (D3). Couvre l'aiguillage : un document ORDINAIRE
/// (sans frais) n'est happé NI par le job marge/taxable/export (qui exigent des frais), et la garde D1 le défère de
/// la voie document. La NATURE (LivraisonBiens/PrestationServices) commande la TT-81 ; Mixte → fail-closed. Base
/// ISOLÉE par méthode.
/// </summary>
public sealed class B2cPlainTaxableReportingJobTests : IAsyncLifetime
{
    private const string SendB2cTransaction = "SendB2cTransactionAsync";
    private const string SendDocument = "SendDocumentAsync";

    // Détail journalisé par le plug-in factice : Catégorie/Rôle/jour (TLB1 facture, TPS1 note — jour de la fixture).
    private const string Tlb1TxDetail = "Tlb1/Seller/20260120";
    private const string Tps1TxDetail = "Tps1/Seller/20260120";

    private readonly PipelineSendHarness _harness = new();

    public Task InitializeAsync() => _harness.InitializeAsync();

    public Task DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Plain_Facture_LivraisonBiens_Is_Reported_As_Tlb1_Not_By_Auction_Jobs()
    {
        // Une facture client B2C (lignes S 20 %, TVA distincte, acheteur particulier, AUCUN frais) : les jobs
        // enchères (marge/taxable/export) l'IGNORENT (ils exigent des frais) ; le job ordinaire l'agrège en TLB1/SE.
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildPlainTaxableInvoice("fc-" + documentId.ToString("N"), OperationCategory.LivraisonBiens);
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        // Les jobs enchères n'émettent RIEN (pas de frais → pas une déclaration agrégée d'enchères).
        await _harness.RunB2cMarginAsync();
        await _harness.RunB2cTaxableAsync();
        await _harness.RunB2cExportAsync();
        _harness.PaCallCount(SendB2cTransaction, Tlb1TxDetail).Should()
            .Be(0, "une facture ordinaire (sans frais) n'est happée par aucun job enchères.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Should()
            .BeEmpty("aucun job enchères n'écrit d'entrée d'émission pour une facture ordinaire.");

        // Le job ORDINAIRE l'émet en TLB1/SE et le transmet ; l'émission est journalisée (Pending → Issued).
        await _harness.RunB2cPlainAsync();
        _harness.PaCallCount(SendB2cTransaction, Tlb1TxDetail).Should()
            .Be(1, "une facture client (livraison de biens) est transmise en TLB1/SE.");
        var emissions = await _harness.GetB2cMarginEmissionsAsync(documentId);
        emissions.Select(e => e.Status).Should().Equal("Pending", "Issued");
        emissions[^1].PaEmissionId.Should().NotBeNullOrWhiteSpace("l'id serveur de la transaction TLB1 est journalisé.");

        // Émission Issued ⇒ le document passe à EReported (canal B2C agrégé ≠ Issued voie document — ADR-0037/BUG-24).
        (await _harness.GetDocumentStateAsync(documentId)).Should()
            .Be("EReported", "l'émission de la contribution TLB1 ordinaire transitionne le document vers EReported (ADR-0037).");
    }

    [Fact]
    public async Task Plain_NoteHonoraires_PrestationServices_Is_Reported_As_Tps1()
    {
        // Une note d'honoraires d'inventaire (prestation de services) → catégorie TT-81 TPS1 (G1.68), DÉRIVÉE de la
        // nature de l'opération (PrestationServices), jamais hard-codée TLB1.
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildPlainTaxableInvoice("nh-" + documentId.ToString("N"), OperationCategory.PrestationServices);
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cPlainAsync();

        _harness.PaCallCount(SendB2cTransaction, Tps1TxDetail).Should()
            .Be(1, "une note d'honoraires (prestation de services) est transmise en TPS1.");
        _harness.PaCallCount(SendB2cTransaction, Tlb1TxDetail).Should()
            .Be(0, "aucune transaction TLB1 pour une prestation de services.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Select(e => e.Status).Should().Equal("Pending", "Issued");
    }

    [Fact]
    public async Task Mixed_Run_Emits_One_Post_Per_Tt81_Category()
    {
        // Un run mêlant une facture (LivraisonBiens → TLB1) et une note (PrestationServices → TPS1) émet DEUX POST
        // distincts — un par TT-81 (le job groupe par catégorie, EmitAllAsync applique UNE catégorie par lot).
        await _harness.UsePublishedFakeAsync();

        var factureId = Guid.NewGuid();
        await _harness.SeedDetectedAndStageAsync(factureId, CheckIntegrationFixtures.BuildPlainTaxableInvoice("fc-" + factureId.ToString("N"), OperationCategory.LivraisonBiens));
        await _harness.MarkReadyToSendAsync(factureId);

        var noteId = Guid.NewGuid();
        await _harness.SeedDetectedAndStageAsync(noteId, CheckIntegrationFixtures.BuildPlainTaxableInvoice("nh-" + noteId.ToString("N"), OperationCategory.PrestationServices));
        await _harness.MarkReadyToSendAsync(noteId);

        await _harness.RunB2cPlainAsync();

        _harness.PaCallCount(SendB2cTransaction, Tlb1TxDetail).Should().Be(1, "la facture part en TLB1.");
        _harness.PaCallCount(SendB2cTransaction, Tps1TxDetail).Should().Be(1, "la note part en TPS1, dans un POST distinct.");
    }

    [Fact]
    public async Task Mixte_OperationCategory_Is_Blocked_FailClosed_And_Never_Sent()
    {
        // Nature Mixte (bien + service non ventilé par ligne) → TT-81 (TLB1/TPS1) non déterminable → FAIL-CLOSED
        // tracé : aucun POST, aucune entrée d'émission (jamais une catégorie devinée — n°2).
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildPlainTaxableInvoice("mx-" + documentId.ToString("N"), OperationCategory.Mixte);
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cPlainAsync();

        _harness.PaCallCount(SendB2cTransaction, Tlb1TxDetail).Should().Be(0, "une nature Mixte ne produit aucune transaction TLB1.");
        _harness.PaCallCount(SendB2cTransaction, Tps1TxDetail).Should().Be(0, "une nature Mixte ne produit aucune transaction TPS1.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Should()
            .BeEmpty("un document à nature indéterminée est bloqué fail-closed — jamais marqué tenté.");
        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("ReadyToSend");
    }

    [Fact]
    public async Task Send_Job_Holds_Plain_Declaration_From_The_Per_Document_Path()
    {
        // Aiguillage D1 (généralisé à IsB2cReportingDeclaration) : une facture B2C marquée 10.3 (SANS frais) est
        // DIFFÉRÉE de la voie document (SendDocumentAsync l'enverrait à tort en Factur-X) ; elle ne part que par le
        // job ordinaire. C'est le cœur du flux #7 — sans cette généralisation, un B2C plat fuyait en e-invoicing.
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildPlainTaxableInvoice("fc-" + documentId.ToString("N"), OperationCategory.LivraisonBiens);
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId)).Should()
            .Be("ReadyToSend", "une facture B2C ordinaire reste ReadyToSend — différée vers le job ordinaire.");
        _harness.PaCallCount(SendDocument, declaration.Number).Should()
            .Be(0, "un document B2C ordinaire ne part JAMAIS par la voie document (jamais SendDocumentAsync / Factur-X).");
    }

    [Fact]
    public async Task Rerun_Does_Not_Re_Emit_An_Already_Issued_Plain_Document()
    {
        // D3 (attempt-once) : un document ordinaire déjà tenté est EXCLU du run suivant — JAMAIS un 2e POST.
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildPlainTaxableInvoice("fc-" + documentId.ToString("N"), OperationCategory.LivraisonBiens);
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cPlainAsync();
        await _harness.RunB2cPlainAsync();

        _harness.PaCallCount(SendB2cTransaction, Tlb1TxDetail).Should()
            .Be(1, "le 2e run exclut le document déjà tenté — jamais 2 POST.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Count(e => e.Status == "Issued").Should()
            .Be(1, "une seule émission Issued, même après ré-exécution.");
    }

    [Fact]
    public async Task Plain_Run_Is_Traced_With_Its_Run_Type()
    {
        // Traçabilité : le run écrit une exécution typée B2cPlainTaxableAggregate au journal (pipeline.run_logs).
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildPlainTaxableInvoice("fc-" + documentId.ToString("N"), OperationCategory.LivraisonBiens);
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cPlainAsync();

        var runs = await _harness.GetRunsAsync();
        var run = runs.First(r => r.RunType == PipelineRunType.B2cPlainTaxableAggregate);
        run.Detail.Should().Contain("document ordinaire", "le run du flux ordinaire est tracé et identifiable (jamais un flux muet).");
    }

    [Fact]
    public async Task Plain_To_Pa_Without_B2cReporting_Capability_Stays_ReadyToSend_And_Is_Never_Sent()
    {
        // Gate du job : une PA sans SupportsB2cReporting ne reçoit RIEN et aucun document n'est marqué tenté
        // (repris au prochain run quand la capacité sera là).
        await _harness.UsePublishedFakeAsync(new FakePaClientOptions { Capabilities = WithoutB2cReporting() });

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildPlainTaxableInvoice("fc-" + documentId.ToString("N"), OperationCategory.LivraisonBiens);
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cPlainAsync();

        _harness.PaCallCount(SendB2cTransaction, Tlb1TxDetail).Should()
            .Be(0, "sans la capacité e-reporting B2C, la facture n'est jamais transmise.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Should()
            .BeEmpty("aucun document n'est marqué tenté quand la capacité est absente (repris au prochain run).");
        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("ReadyToSend");
    }

    /// <summary>Capacités d'une PA publiée générale MAIS sans la capacité e-reporting B2C (le reste = défaut V1).</summary>
    private static PaCapabilities WithoutB2cReporting() => new()
    {
        PaName = "Fake",
        SupportsB2cReporting = false,
        SupportsDomesticPaymentReporting = true,
        SupportsInternationalPaymentReporting = false,
        SupportsB2bInvoicing = false,
        SupportsCreditNotes = true,
        SupportsTaxReportRetrieval = true,
        SupportsDocumentRetrieval = true,
        SupportsReportRectification = true,
        SupportsSelfBilling = true,
        MaxDocumentsPerRequest = null,
    };
}
