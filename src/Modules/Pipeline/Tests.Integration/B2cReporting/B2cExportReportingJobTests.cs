namespace Liakont.Modules.Pipeline.Tests.Integration.B2cReporting;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Tests.Integration.Check;
using Liakont.Modules.Pipeline.Tests.Integration.Send;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Fake;
using Xunit;

/// <summary>
/// BUG-11 — JOB e-reporting B2C d'EXPORT HORS UE détaxé (flux 10.3, enchères, art. 262 I) sur base réelle
/// (Testcontainers) : découverte des déclarations d'export prêtes → constitution d'UNE transaction UNITAIRE par
/// opération (base HT exonérée = adjudication + commission acheteur, taux 0) → transmission PA (TLB1/SE) →
/// journal d'émission APPEND-ONLY (attempt-once par document, D3) → gel du lien reporting↔pièce (D2). Couvre
/// l'aiguillage CRUCIAL : un export (TotalTax == 0 + catégorie G) N'EST PAS happé par le job MARGE
/// (TotalTax == 0 partagé, mais exclu par !IsExportDeclaration) ni par le job TAXABLE (TotalTax > 0), et la
/// garde D1 le défère de la voie document. Base ISOLÉE par méthode.
/// </summary>
public sealed class B2cExportReportingJobTests : IAsyncLifetime
{
    private const string SendB2cTransaction = "SendB2cTransactionAsync";
    private const string SendDocument = "SendDocumentAsync";

    // Détail journalisé par le plug-in factice pour la transaction d'export/franchise (TLB1/SE, jour de la fixture).
    private const string ExportTxDetail = "Tlb1/Seller/20260120";

    // Détail de la transaction intracom (TNT1/SE — non soumis en France, jour de la fixture).
    private const string IntracomTxDetail = "Tnt1/Seller/20260120";

    // Détail de la transaction de MARGE (TMA1/SE) — vérifie que le job marge n'émet RIEN pour un export.
    private const string MarginTxDetail = "Tma1/Seller/20260120";

    private readonly PipelineSendHarness _harness = new();

    public Task InitializeAsync() => _harness.InitializeAsync();

    public Task DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Export_Is_Reported_Unitaire_By_The_Export_Job_Not_Margin_Nor_Taxable()
    {
        // Un export hors UE (adjudication G détaxée, TotalTax == 0, commission acheteur, acheteur particulier) :
        // le job MARGE l'IGNORE (exclu par !IsExportDeclaration), le job TAXABLE l'IGNORE (TotalTax == 0), et le
        // job EXPORT l'émet en UNE transaction TLB1/SE (taux 0), avec journal d'émission attempt-once.
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildExportAuctionWithFees("ba-" + documentId.ToString("N"));
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        // Le job MARGE ne touche pas un export (TotalTax == 0 partagé, mais catégorie G ≠ E → exclu).
        await _harness.RunB2cMarginAsync();
        _harness.PaCallCount(SendB2cTransaction, MarginTxDetail).Should()
            .Be(0, "un export détaxé (catégorie G) n'est pas une marge → le job marge l'exclut.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Should()
            .BeEmpty("le job marge n'écrit aucune entrée d'émission pour un export.");

        // Le job TAXABLE ne touche pas un export (TotalTax == 0 ⇒ pas un régime de prix total).
        await _harness.RunB2cTaxableAsync();
        _harness.PaCallCount(SendB2cTransaction, ExportTxDetail).Should()
            .Be(0, "un export (TotalTax == 0) n'est pas un prix total taxable → le job taxable l'ignore.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Should()
            .BeEmpty("le job taxable n'écrit aucune entrée d'émission pour un export.");

        // Le job EXPORT l'émet en TLB1/SE UNITAIRE et le transmet ; l'émission est journalisée (Pending → Issued).
        await _harness.RunB2cExportAsync();
        _harness.PaCallCount(SendB2cTransaction, ExportTxDetail).Should()
            .Be(1, "l'export est transmis en UNE transaction unitaire (TLB1/SE, taux 0).");
        var emissions = await _harness.GetB2cMarginEmissionsAsync(documentId);
        emissions.Select(e => e.Status).Should().Equal("Pending", "Issued");
        emissions[^1].PaEmissionId.Should().NotBeNullOrWhiteSpace("l'id serveur de la transaction TLB1 est journalisé.");

        // Émission Issued ⇒ le document passe à EReported (canal B2C agrégé, voie e-reporting distincte de
        // Issued voie document — ADR-0037/BUG-24).
        (await _harness.GetDocumentStateAsync(documentId)).Should()
            .Be("EReported", "l'émission de la contribution export transitionne le document vers EReported (ADR-0037).");
    }

    [Fact]
    public async Task Intracom_Cee_Is_Reported_As_Tnt1()
    {
        // CEE → intracommunautaire (262 ter / 258 A) → catégorie K → TT-81 TNT1 (non soumis en France), taux 0.
        // La TT-81 est DÉRIVÉE de la catégorie mappée (jamais hard-codée TLB1).
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildExportAuctionWithFees("ba-" + documentId.ToString("N"), "EXP_CEE");
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cExportAsync();

        _harness.PaCallCount(SendB2cTransaction, IntracomTxDetail).Should()
            .Be(1, "un intracom (catégorie K) est transmis en TNT1, jamais TLB1.");
        _harness.PaCallCount(SendB2cTransaction, ExportTxDetail).Should()
            .Be(0, "aucune transaction TLB1 pour un intracom.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Select(e => e.Status).Should().Equal("Pending", "Issued");
    }

    [Fact]
    public async Task Franchise_France_Is_Reported_As_Tlb1()
    {
        // FRANCE + code_export → franchise (art. 275, export-bound) → catégorie G → TT-81 TLB1, taux 0.
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildExportAuctionWithFees("ba-" + documentId.ToString("N"), "EXP_FR");
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cExportAsync();

        _harness.PaCallCount(SendB2cTransaction, ExportTxDetail).Should()
            .Be(1, "une franchise export (catégorie G) est transmise en TLB1, taux 0.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Select(e => e.Status).Should().Equal("Pending", "Issued");
    }

    [Fact]
    public async Task Mixed_Run_Emits_One_Post_Per_Tt81_Category()
    {
        // Un run mêlant un export hors UE (G→TLB1) et un intracom (K→TNT1) émet DEUX POST distincts — un par TT-81
        // (EmitAllAsync applique UNE catégorie par lot ; le job groupe par catégorie).
        await _harness.UsePublishedFakeAsync();

        var exportId = Guid.NewGuid();
        await _harness.SeedDetectedAndStageAsync(exportId, CheckIntegrationFixtures.BuildExportAuctionWithFees("ba-x-" + exportId.ToString("N"), "EXP_HORSUE"));
        await _harness.MarkReadyToSendAsync(exportId);

        var intracomId = Guid.NewGuid();
        await _harness.SeedDetectedAndStageAsync(intracomId, CheckIntegrationFixtures.BuildExportAuctionWithFees("ba-i-" + intracomId.ToString("N"), "EXP_CEE"));
        await _harness.MarkReadyToSendAsync(intracomId);

        await _harness.RunB2cExportAsync();

        _harness.PaCallCount(SendB2cTransaction, ExportTxDetail).Should().Be(1, "l'export hors UE part en TLB1.");
        _harness.PaCallCount(SendB2cTransaction, IntracomTxDetail).Should().Be(1, "l'intracom part en TNT1, dans un POST distinct.");
    }

    [Fact]
    public async Task Issued_Export_Freezes_Reversible_Reporting_Piece_Link()
    {
        // D2/B6 : APRÈS confirmation, le lien reporting↔pièce est gelé au grain DOCUMENT (réversibilité préservée).
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var sourceReference = "ba-" + documentId.ToString("N");
        var declaration = CheckIntegrationFixtures.BuildExportAuctionWithFees(sourceReference);
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cExportAsync();

        var links = await _harness.GetReportingPieceLinksAsync(documentId);
        links.Should().ContainSingle("un export transmis gèle exactement un lien vers la pièce source.");
        links.Single().SourceReference.Should().Be(sourceReference);
        links.Single().DocumentId.Should().Be(documentId);
    }

    [Fact]
    public async Task Rerun_Does_Not_Re_Emit_An_Already_Issued_Export()
    {
        // D3 (attempt-once) : un export déjà tenté est EXCLU du run suivant — JAMAIS un 2e POST.
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildExportAuctionWithFees("ba-" + documentId.ToString("N"));
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cExportAsync();
        await _harness.RunB2cExportAsync();

        _harness.PaCallCount(SendB2cTransaction, ExportTxDetail).Should()
            .Be(1, "le 2e run exclut l'export déjà tenté — jamais 2 POST.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Count(e => e.Status == "Issued").Should()
            .Be(1, "une seule émission Issued, même après ré-exécution.");
    }

    [Fact]
    public async Task Send_Job_Holds_Export_Declaration_From_The_Per_Document_Path()
    {
        // Aiguillage D1 (généralisé) : un export est une déclaration B2C agrégée → SendTenantJob le DIFFÈRE de la
        // voie document (SendDocumentAsync le rejetterait — pas de destinataire) ; il ne part que par le job export.
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildExportAuctionWithFees("ba-" + documentId.ToString("N"));
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId)).Should()
            .Be("ReadyToSend", "un export reste ReadyToSend — différé vers le job export.");
        _harness.PaCallCount(SendDocument, declaration.Number).Should()
            .Be(0, "un export ne part JAMAIS par la voie document (jamais SendDocumentAsync).");
    }

    [Fact]
    public async Task Export_Run_Is_Traced_With_Its_Run_Type()
    {
        // Traçabilité : le run écrit une exécution typée B2cExportReporting au journal (pipeline.run_logs).
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildExportAuctionWithFees("ba-" + documentId.ToString("N"));
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cExportAsync();

        var runs = await _harness.GetRunsAsync();
        var run = runs.First(r => r.RunType == PipelineRunType.B2cExportReporting);
        run.Detail.Should().Contain("exonéré international", "le run d'exonéré international est tracé et identifiable (jamais un flux muet).");
    }

    [Fact]
    public async Task Export_To_Pa_Without_B2cReporting_Capability_Stays_ReadyToSend_And_Is_Never_Sent()
    {
        // Gate du job EXPORT : une PA sans SupportsB2cReporting ne reçoit RIEN et aucun document n'est marqué tenté
        // (repris au prochain run). L'export est de l'e-reporting B2C ordinaire (TLB1) — pas de capacité « marge ».
        await _harness.UsePublishedFakeAsync(new FakePaClientOptions { Capabilities = WithoutB2cReporting() });

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildExportAuctionWithFees("ba-" + documentId.ToString("N"));
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cExportAsync();

        _harness.PaCallCount(SendB2cTransaction, ExportTxDetail).Should()
            .Be(0, "sans la capacité e-reporting B2C, l'export n'est jamais transmis.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Should()
            .BeEmpty("aucun document n'est marqué tenté quand la capacité est absente (repris au prochain run).");
        (await _harness.GetDocumentStateAsync(documentId)).Should()
            .Be("ReadyToSend", "le document reste prêt à l'envoi (le job ne transitionne pas la machine à états).");
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
