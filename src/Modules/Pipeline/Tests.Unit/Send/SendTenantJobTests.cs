namespace Liakont.Modules.Pipeline.Tests.Unit.Send;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Infrastructure.Send;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Fake;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Jobs;
using Xunit;

/// <summary>
/// Comportement du job SEND (PIP01c) avec des doubles : diagnostic PA, dry-run, issues d'envoi (Issued /
/// Rejected) et anti-doublon (raccrochage d'un Sending déjà connu de la PA sans renvoi). Le flux réel
/// (base + archive WORM + staging) est couvert par les tests d'intégration.
/// </summary>
public sealed class SendTenantJobTests
{
    private static readonly Guid TenantCompany = Guid.NewGuid();

    [Fact]
    public async Task No_Active_Pa_Account_Writes_RunLog_And_Sends_Nothing()
    {
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        var lifecycle = new SendTestDoubles.RecordingDocumentLifecycle();
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var fake = new FakePaClient();
        var provider = BuildProvider(
            new SendTestDoubles.ConfigurableTenantSettingsQueries(TenantCompany, Array.Empty<Liakont.Modules.TenantSettings.Contracts.DTOs.PaAccountDto>()),
            queries, lifecycle, new SendTestDoubles.MapStagingStore(), new SendTestDoubles.RecordingStagingPurgeService(true),
            new SendTestDoubles.RecordingArchiveService(), runLogs, fake);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        fake.Calls.Should().BeEmpty("aucun compte actif : on n'interroge même pas la PA.");
        lifecycle.BeganSending.Should().BeEmpty();
        runLogs.Saved.Should().ContainSingle();
        runLogs.Saved[0].Detail.Should().Contain("aucun compte");
    }

    [Fact]
    public async Task Inactive_TaxReportSetting_Warns_And_Sends_Nothing()
    {
        var (id, queries, lifecycle, staging) = SeedSingle("ReadyToSend");
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var purge = new SendTestDoubles.RecordingStagingPurgeService(true);
        var archive = new SendTestDoubles.RecordingArchiveService();
        var fake = new FakePaClient(); // tax_report_setting NON publié (StartDate null) par défaut.
        var provider = BuildProvider(ActiveAccountSettings(), queries, lifecycle, staging, purge, archive, runLogs, fake);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.BeganSending.Should().BeEmpty("SIREN non publié = aucun envoi à l'aveugle.");
        fake.IssuedDocumentNumbers.Should().BeEmpty();
        runLogs.Saved.Should().ContainSingle();
        runLogs.Saved[0].Detail.Should().Contain("SIREN non publié");
        _ = id;
    }

    [Fact]
    public async Task DryRun_Counts_Ready_But_Performs_No_Pa_Write()
    {
        var (id, queries, lifecycle, staging) = SeedSingle("ReadyToSend");
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var fake = await PublishedFakeAsync(FakePaScenario.Success);
        var provider = BuildProvider(ActiveAccountSettings(), queries, lifecycle, staging, new SendTestDoubles.RecordingStagingPurgeService(true), new SendTestDoubles.RecordingArchiveService(), runLogs, fake);

        await new SendTenantJob(PipelineRunTrigger.Manual, dryRun: true).ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.BeganSending.Should().BeEmpty();
        fake.IssuedDocumentNumbers.Should().BeEmpty();
        fake.Calls.Should().NotContain(c => c.Method == nameof(IPaClient.SendDocumentAsync), "le dry-run n'effectue aucune écriture PA.");
        runLogs.Saved.Should().ContainSingle();
        runLogs.Saved[0].Detail.Should().Contain("dry-run");
        runLogs.Saved[0].DocumentsProcessed.Should().Be(1);
        _ = id;
    }

    [Fact]
    public async Task ReadyToSend_Issued_Archives_Then_Purges_Staging()
    {
        var (id, queries, lifecycle, staging) = SeedSingle("ReadyToSend");
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var purge = new SendTestDoubles.RecordingStagingPurgeService(wormPresent: true);
        var archive = new SendTestDoubles.RecordingArchiveService();
        var fake = await PublishedFakeAsync(FakePaScenario.Success);
        var provider = BuildProvider(ActiveAccountSettings(), queries, lifecycle, staging, purge, archive, runLogs, fake);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.BeganSending.Should().ContainSingle().Which.Should().Be(id);
        lifecycle.Issued.Should().ContainSingle().Which.Should().Be(id);
        archive.Requests.Should().ContainSingle();
        purge.Calls.Should().ContainSingle("la purge du staging est subordonnée au paquet WORM.");
        fake.IssuedDocumentNumbers.Should().ContainSingle();
        runLogs.Saved[^1].RunType.Should().Be(PipelineRunType.Send);
        runLogs.Saved[^1].DocumentsSucceeded.Should().Be(1);
    }

    [Fact]
    public async Task ReadyToSend_With_Unresolvable_Emitter_Is_Held_Not_Transmitted()
    {
        // RB9 / garde « bloquer plutôt qu'envoyer faux » : le pivot stagé n'a pas d'émetteur et le profil tenant
        // n'en fournit pas (vidé entre le CHECK et le SEND) → enrichissement read-time = émetteur toujours nul →
        // HOLD : on ne transmet PAS un document sans vendeur (et l'archive ne déréférence jamais un Supplier nul).
        var id = Guid.NewGuid();
        var document = SendTestData.Document(id, "ReadyToSend");
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        queries.AddDocument(document);
        queries.AddInState("ReadyToSend", SendTestData.Summary(id, "ReadyToSend"));
        var staging = new SendTestDoubles.MapStagingStore();
        staging.Stage(id, CanonicalJson.Serialize(SendTestData.SupplierLessPivot(document.DocumentNumber)));

        var lifecycle = new SendTestDoubles.RecordingDocumentLifecycle();
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var archive = new SendTestDoubles.RecordingArchiveService();
        var fake = await PublishedFakeAsync(FakePaScenario.Success);

        // Profil tenant SANS émetteur (null) : l'enrichissement read-time ne peut pas résoudre le vendeur.
        var tenantSettings = new SendTestDoubles.ConfigurableTenantSettingsQueries(TenantCompany, new[] { SendTestData.ActiveAccount() });
        var provider = BuildProvider(tenantSettings, queries, lifecycle, staging, new SendTestDoubles.RecordingStagingPurgeService(true), archive, runLogs, fake);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.BeganSending.Should().BeEmpty("émetteur non résolu = aucun envoi (HOLD).");
        lifecycle.Issued.Should().BeEmpty();
        fake.IssuedDocumentNumbers.Should().BeEmpty();
        fake.Calls.Should().NotContain(c => c.Method == nameof(IPaClient.SendDocumentAsync), "on ne transmet jamais un document sans émetteur.");
        archive.Requests.Should().BeEmpty("aucune archive d'un document non transmis.");
    }

    [Fact]
    public async Task ReadyToSend_With_Null_Supplier_Is_Filled_From_Tenant_Profile_And_Transmitted()
    {
        // RB9 : l'émetteur est rempli au READ-TIME depuis le profil tenant. Pivot stagé SANS vendeur + profil
        // renseigné → enrichissement → vendeur résolu → transmission nominale. Preuve que le SEND appelle bien
        // l'enrichisseur : sans lui, la garde EmitterUnresolved bloquerait l'envoi (cf. test HOLD ci-dessus).
        var id = Guid.NewGuid();
        var document = SendTestData.Document(id, "ReadyToSend");
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        queries.AddDocument(document);
        queries.AddInState("ReadyToSend", SendTestData.Summary(id, "ReadyToSend"));
        var staging = new SendTestDoubles.MapStagingStore();
        staging.Stage(id, CanonicalJson.Serialize(SendTestData.SupplierLessPivot(document.DocumentNumber)));

        var lifecycle = new SendTestDoubles.RecordingDocumentLifecycle();
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var archive = new SendTestDoubles.RecordingArchiveService();
        var fake = await PublishedFakeAsync(FakePaScenario.Success);
        var tenantSettings = new SendTestDoubles.ConfigurableTenantSettingsQueries(
            TenantCompany,
            new[] { SendTestData.ActiveAccount() },
            SendTestData.EmitterProfile(),
            SendTestData.FiscalSettings());
        var provider = BuildProvider(tenantSettings, queries, lifecycle, staging, new SendTestDoubles.RecordingStagingPurgeService(true), archive, runLogs, fake);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.BeganSending.Should().ContainSingle().Which.Should().Be(id, "émetteur rempli depuis le profil → envoi nominal.");
        lifecycle.Issued.Should().ContainSingle().Which.Should().Be(id);
        fake.IssuedDocumentNumbers.Should().ContainSingle();
    }

    [Fact]
    public async Task B2cReportingDeclaration_To_Pa_Without_Capability_Is_Held_Not_Transmitted()
    {
        // B2C01 : une déclaration e-reporting B2C (flux 10.3) vers une PA qui ne déclare PAS SupportsB2cReporting
        // reste ReadyToSend (maintenue, jamais transmise — résultat typé journalisé, CLAUDE.md n°3). Émetteur
        // présent dans le pivot → le SEUL motif de maintien est l'absence de capacité B2C.
        var id = Guid.NewGuid();
        var document = SendTestData.Document(id, "ReadyToSend");
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        queries.AddDocument(document);
        queries.AddInState("ReadyToSend", SendTestData.Summary(id, "ReadyToSend"));
        var staging = new SendTestDoubles.MapStagingStore();
        staging.Stage(id, CanonicalJson.Serialize(SendTestData.B2cReportingDeclarationPivot(document.DocumentNumber)));

        var lifecycle = new SendTestDoubles.RecordingDocumentLifecycle();
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var archive = new SendTestDoubles.RecordingArchiveService();
        var fake = await PublishedFakeWithoutB2cReportingAsync();
        var provider = BuildProvider(ActiveAccountSettings(), queries, lifecycle, staging, new SendTestDoubles.RecordingStagingPurgeService(true), archive, runLogs, fake);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.BeganSending.Should().BeEmpty("capacité B2C absente = aucun envoi (HOLD).");
        lifecycle.Issued.Should().BeEmpty();
        fake.IssuedDocumentNumbers.Should().BeEmpty();
        fake.Calls.Should().NotContain(c => c.Method == nameof(IPaClient.SendDocumentAsync), "on ne transmet jamais une déclaration 10.3 sans la capacité B2C.");
        archive.Requests.Should().BeEmpty("aucune archive d'un document non transmis.");
    }

    [Fact]
    public async Task Ordinary_Invoice_To_Pa_Without_B2cReporting_Is_Still_Transmitted()
    {
        // B2C01 — NON-RÉGRESSION : la garde 10.3 est CIBLÉE sur le marqueur. Une facture ORDINAIRE (marqueur
        // faux) vers la MÊME PA sans capacité B2C est TOUJOURS transmise — la garde ne touche pas la voie
        // unique SendDocumentAsync des autres flux.
        var (id, queries, lifecycle, staging) = SeedSingle("ReadyToSend");
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var archive = new SendTestDoubles.RecordingArchiveService();
        var fake = await PublishedFakeWithoutB2cReportingAsync();
        var provider = BuildProvider(ActiveAccountSettings(), queries, lifecycle, staging, new SendTestDoubles.RecordingStagingPurgeService(true), archive, runLogs, fake);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.Issued.Should().ContainSingle().Which.Should().Be(id, "une facture ordinaire (sans marqueur 10.3) n'est jamais touchée par la garde B2C.");
        fake.IssuedDocumentNumbers.Should().ContainSingle();
    }

    [Fact]
    public async Task B2cReportingDeclaration_To_Pa_With_Capability_Is_Routed_And_Issued()
    {
        // B2C01 — chemin NOMINAL POSITIF : une déclaration 10.3 vers une PA qui DÉCLARE SupportsB2cReporting est
        // routée par la voie document unique (SendDocumentAsync) et émise. Preuve que la garde est CIBLÉE (ne
        // bloque pas quand la capacité est présente) et que le marqueur n'empêche pas le routage.
        var id = Guid.NewGuid();
        var document = SendTestData.Document(id, "ReadyToSend");
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        queries.AddDocument(document);
        queries.AddInState("ReadyToSend", SendTestData.Summary(id, "ReadyToSend"));
        var staging = new SendTestDoubles.MapStagingStore();
        staging.Stage(id, CanonicalJson.Serialize(SendTestData.B2cReportingDeclarationPivot(document.DocumentNumber)));

        var lifecycle = new SendTestDoubles.RecordingDocumentLifecycle();
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var archive = new SendTestDoubles.RecordingArchiveService();
        var fake = await PublishedFakeAsync(FakePaScenario.Success); // capacités par défaut : SupportsB2cReporting = true.
        var provider = BuildProvider(ActiveAccountSettings(), queries, lifecycle, staging, new SendTestDoubles.RecordingStagingPurgeService(true), archive, runLogs, fake);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.Issued.Should().ContainSingle().Which.Should().Be(id, "une déclaration 10.3 vers une PA capable est routée et émise.");
        fake.IssuedDocumentNumbers.Should().Contain(document.DocumentNumber);
    }

    [Fact]
    public async Task B2cReportingDeclaration_In_Sending_Already_Transmitted_Is_Reconciled_Even_If_Capability_Removed()
    {
        // B2C01 — robustesse : un document Sending a forcément été TRANSMIS (capacité B2C présente à l'envoi). Si
        // la capacité est retirée après coup, la reprise doit le RÉCONCILIER (finalisation anti-doublon depuis le
        // statut PA) — JAMAIS le figer en Sending. La garde 10.3 ne retient QUE la re-transmission, pas la
        // finalisation d'un document déjà parti.
        var id = Guid.NewGuid();
        const string number = "B2C-2026-0123";
        var document = SendTestData.Document(id, "Sending", number: number, payloadHash: "hash-b2c-123", paDocumentId: "FAKE-" + number);
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        queries.AddDocument(document);
        queries.AddPotentiallySent(SendTestData.Summary(id, "Sending", number));

        var pivot = SendTestData.B2cReportingDeclarationPivot(number);
        var staging = new SendTestDoubles.MapStagingStore();
        staging.Stage(id, CanonicalJson.Serialize(pivot));

        var lifecycle = new SendTestDoubles.RecordingDocumentLifecycle();
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var archive = new SendTestDoubles.RecordingArchiveService();
        var fake = await PublishedFakeWithoutB2cReportingAsync(); // capacité B2C RETIRÉE depuis l'envoi.

        // Cycle N : la déclaration a bien été transmise (la PA la connaît comme Issued), réponse perdue → restée Sending.
        await fake.SendDocumentAsync(pivot);
        var sendCallsBefore = fake.Calls.Count(c => c.Method == nameof(IPaClient.SendDocumentAsync));

        var provider = BuildProvider(ActiveAccountSettings(), queries, lifecycle, staging, new SendTestDoubles.RecordingStagingPurgeService(true), archive, runLogs, fake);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.Issued.Should().ContainSingle().Which.Should().Be(id, "un document déjà transmis est finalisé (réconcilié), jamais figé par la garde.");
        fake.Calls.Count(c => c.Method == nameof(IPaClient.SendDocumentAsync))
            .Should().Be(sendCallsBefore, "réconciliation anti-doublon : aucune RE-transmission.");
    }

    [Fact]
    public async Task B2cReportingDeclaration_In_Sending_Without_Capability_Stays_Sending_Not_Resent()
    {
        // B2C01 — chemin REPRISE (RecoverSendingAsync) : une déclaration 10.3 restée Sending (crash) vers une PA
        // sans capacité B2C est MAINTENUE Sending (aucune finalisation, aucune retransmission) — la garde ciblée
        // s'applique aussi sur ce chemin, jamais un faux Issued/Failed.
        var id = Guid.NewGuid();
        const string number = "B2C-2026-0099";
        var document = SendTestData.Document(id, "Sending", number: number, payloadHash: "hash-b2c-99");
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        queries.AddDocument(document);
        queries.AddPotentiallySent(SendTestData.Summary(id, "Sending", number));
        var staging = new SendTestDoubles.MapStagingStore();
        staging.Stage(id, CanonicalJson.Serialize(SendTestData.B2cReportingDeclarationPivot(number)));

        var lifecycle = new SendTestDoubles.RecordingDocumentLifecycle();
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var archive = new SendTestDoubles.RecordingArchiveService();
        var fake = await PublishedFakeWithoutB2cReportingAsync();
        var provider = BuildProvider(ActiveAccountSettings(), queries, lifecycle, staging, new SendTestDoubles.RecordingStagingPurgeService(true), archive, runLogs, fake);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.Issued.Should().BeEmpty("capacité B2C absente = aucune finalisation depuis Sending.");
        lifecycle.Rejected.Should().BeEmpty("maintenu, jamais un faux rejet.");
        lifecycle.TechnicalError.Should().BeEmpty("maintenu, jamais une fausse erreur technique.");
        fake.Calls.Should().NotContain(c => c.Method == nameof(IPaClient.SendDocumentAsync), "aucune retransmission d'une déclaration 10.3 sans la capacité.");
        archive.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task B2cReportingDeclaration_In_TechnicalError_Without_Capability_Stays_And_Is_Not_Repromoted()
    {
        // B2C01 — chemin RETRY (RetryTechnicalErrorAsync) : une déclaration 10.3 en TechnicalError vers une PA
        // sans capacité B2C est MAINTENUE (jamais re-promue en ReadyToSend ni retransmise) — la garde ciblée
        // s'applique AVANT la reprise (pas de boucle de retry).
        var id = Guid.NewGuid();
        const string number = "B2C-2026-0111";
        var document = SendTestData.Document(id, "TechnicalError", number: number, payloadHash: "hash-b2c-111");
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        queries.AddDocument(document);
        queries.AddInState("TechnicalError", SendTestData.Summary(id, "TechnicalError", number));
        var staging = new SendTestDoubles.MapStagingStore();
        staging.Stage(id, CanonicalJson.Serialize(SendTestData.B2cReportingDeclarationPivot(number)));

        var lifecycle = new SendTestDoubles.RecordingDocumentLifecycle();
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var archive = new SendTestDoubles.RecordingArchiveService();
        var fake = await PublishedFakeWithoutB2cReportingAsync();
        var provider = BuildProvider(ActiveAccountSettings(), queries, lifecycle, staging, new SendTestDoubles.RecordingStagingPurgeService(true), archive, runLogs, fake);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.ReadyToSend.Should().BeEmpty("capacité B2C absente = jamais re-promue en ReadyToSend (la garde précède la reprise).");
        lifecycle.BeganSending.Should().BeEmpty();
        lifecycle.Issued.Should().BeEmpty();
        fake.Calls.Should().NotContain(c => c.Method == nameof(IPaClient.SendDocumentAsync), "aucune retransmission d'une déclaration 10.3 sans la capacité.");
        archive.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadyToSend_With_Unmapped_Tva_Regime_Is_Held_Not_Transmitted()
    {
        // Miroir TVA du test émetteur (..._Unresolvable_Emitter_Is_Held_Not_Transmitted) : le mapping TVA est
        // reposé au READ-TIME au SEND (symétrique à l'émetteur). Si un régime de la fixture n'est plus couvert
        // par la table validée (table modifiée entre CHECK et SEND), l'évaluation est BLOQUANTE → HOLD :
        // aucune transmission, aucune archive, run DÉFÉRÉ (jamais Failed, repris au prochain cycle).
        var (id, queries, lifecycle, staging) = SeedSingle("ReadyToSend");
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var purge = new SendTestDoubles.RecordingStagingPurgeService(true);
        var archive = new SendTestDoubles.RecordingArchiveService();
        var fake = await PublishedFakeAsync(FakePaScenario.Success);

        // Mapping BLOQUANT : le régime de la fixture n'est plus couvert (StagedReadStatus.TvaUnresolved).
        var provider = BuildProvider(
            ActiveAccountSettings(), queries, lifecycle, staging, purge, archive, runLogs, fake,
            tvaMapping: SendTestDoubles.FakeTvaMappingService.Blocking());

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.BeganSending.Should().BeEmpty("catégorie TVA non reposée = aucun envoi (HOLD).");
        lifecycle.Issued.Should().BeEmpty();
        lifecycle.TechnicalError.Should().BeEmpty("un HOLD est DÉFÉRÉ, jamais une erreur technique.");
        lifecycle.Rejected.Should().BeEmpty();
        fake.IssuedDocumentNumbers.Should().BeEmpty();
        fake.Calls.Should().NotContain(c => c.Method == nameof(IPaClient.SendDocumentAsync), "on ne transmet jamais un document sans catégorie TVA.");
        archive.Requests.Should().BeEmpty("aucune archive d'un document non transmis.");
        purge.Calls.Should().BeEmpty();

        // Run DÉFÉRÉ : le document est compté mais ni succès ni échec (Deferred), jamais Failed.
        runLogs.Saved.Should().ContainSingle();
        runLogs.Saved[^1].DocumentsSucceeded.Should().Be(0);
        runLogs.Saved[^1].DocumentsFailed.Should().Be(0, "un HOLD différé n'est pas un échec.");
    }

    [Fact]
    public async Task Pa_Sending_Response_Is_Deferred_Not_Failed()
    {
        // SuperPDP est ASYNCHRONE (F14 §3.4) : un POST 200 = facture TÉLÉVERSÉE (api:uploaded), pas encore
        // émise. Le plug-in renvoie PaSendState.Sending : le document RESTE Sending (déjà engagé), l'outcome
        // est DÉFÉRÉ — NI succès NI erreur technique (un faux échec afficherait une facture pourtant acceptée).
        var (id, queries, lifecycle, staging) = SeedSingle("ReadyToSend");
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var purge = new SendTestDoubles.RecordingStagingPurgeService(true);
        var archive = new SendTestDoubles.RecordingArchiveService();
        var pa = new SendTestDoubles.SendingPaClient(); // POST 200 → PaSendState.Sending (téléversée, asynchrone).

        var provider = new SendTestDoubles.FakeServiceProvider()
            .Add<TimeProvider>(new SendTestDoubles.FixedTimeProvider(SendTestData.Now))
            .Add<ILogger<SendTenantJob>>(NullLogger<SendTenantJob>.Instance)
            .Add<Liakont.Modules.TenantSettings.Contracts.Queries.ITenantSettingsQueries>(ActiveAccountSettings())
            .Add<IPaClientRegistry>(new SendTestDoubles.StubPaClientRegistry(pa))
            .Add<Liakont.Modules.Documents.Contracts.Queries.IDocumentQueries>(queries)
            .Add<Liakont.Modules.Documents.Contracts.Lifecycle.IDocumentLifecycle>(lifecycle)
            .Add<Liakont.Modules.Documents.Contracts.Queries.IPaTransmissionJournalQueries>(new SendTestDoubles.StubPaTransmissionJournalQueries())
            .Add<Liakont.Modules.Staging.Contracts.IPayloadStagingStore>(staging)
            .Add<Liakont.Modules.Staging.Contracts.IStagingPurgeService>(purge)
            .Add<Liakont.Modules.Archive.Contracts.IArchiveService>(archive)
            .Add<Liakont.Modules.TvaMapping.Contracts.Services.ITvaMappingService>(SendTestDoubles.FakeTvaMappingService.Mapping())
            .Add<Liakont.Modules.Pipeline.Application.IPipelineRunLogStore>(runLogs);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        pa.SendCount.Should().Be(1, "le document est bien téléversé (POST effectué).");
        lifecycle.BeganSending.Should().ContainSingle().Which.Should().Be(id, "le document est engagé Sending avant le POST.");
        lifecycle.Issued.Should().BeEmpty("api:uploaded ≠ émise : pas de finalisation Issued sur le seul POST.");
        lifecycle.TechnicalError.Should().BeEmpty("un téléversement asynchrone n'est JAMAIS une erreur technique (faux échec opérateur).");
        lifecycle.Rejected.Should().BeEmpty();
        archive.Requests.Should().BeEmpty("rien n'est archivé tant que la PA n'a pas confirmé l'émission.");
        purge.Calls.Should().BeEmpty();

        // Outcome DÉFÉRÉ : compté, ni succès ni échec.
        runLogs.Saved.Should().ContainSingle();
        runLogs.Saved[^1].DocumentsSucceeded.Should().Be(0);
        runLogs.Saved[^1].DocumentsFailed.Should().Be(0, "un Sending asynchrone est différé, pas en échec.");
    }

    [Fact]
    public async Task ReadyToSend_With_FacturX_Capable_Pa_Generates_Transmits_Journals_And_Traces()
    {
        // Chemin « Essentiel » bout-en-bout (FX07, F16 §6.1/§7) : facture B2C → Factur-X généré AVANT
        // transmission (piloté par SupportsFacturXTransmission, jamais if (pa is X)) → transmis (artefact
        // pré-construit passé tel quel au plug-in) → journalisé (F06) + trace de support.
        var (id, queries, lifecycle, staging) = SeedSingle("ReadyToSend");
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var purge = new SendTestDoubles.RecordingStagingPurgeService(wormPresent: true);
        var archive = new SendTestDoubles.RecordingArchiveService();
        var pa = new SendTestDoubles.FacturXCapablePaClient();
        var artifact = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 factur-x e2e");
        var builder = new SendTestDoubles.StubFacturXArtifactBuilder(artifact);
        var journal = new SendTestDoubles.RecordingPaTransmissionJournal();
        var trace = new SendTestDoubles.RecordingSupportTraceStore();

        var account = SendTestData.ActiveAccount("generique");
        var provider = BuildFacturXProvider(
            new SendTestDoubles.ConfigurableTenantSettingsQueries(TenantCompany, new[] { account }),
            queries, lifecycle, staging, purge, archive, runLogs, pa, builder, journal, trace);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        // 1) GÉNÉRATION à l'étape Sending, AVANT transmission, pilotée par la capacité.
        builder.BuildCount.Should().Be(1, "le Factur-X est généré à l'étape Sending (jamais dans FinalizeIssuedAsync).");

        // 2) TRANSMISSION de l'artefact pré-construit, identique (jamais régénéré côté plug-in).
        pa.SendCount.Should().Be(1);
        pa.ReceivedArtifact.Should().Equal(artifact, "le plug-in reçoit l'artefact pré-construit via le PaSendContext.");
        lifecycle.Issued.Should().ContainSingle().Which.Should().Be(id);

        // 3) JOURNALISATION (F06 append-only) avec empreinte de l'artefact + clé d'idempotence recherchable.
        var documentNumber = (await queries.GetByIdAsync(id))!.DocumentNumber;
        journal.Entries.Should().ContainSingle();
        var entry = journal.Entries[0];
        entry.DocumentId.Should().Be(id);
        entry.PaPluginId.Should().Be(account.PluginType);
        entry.PaAccount.Should().Be(account.AccountIdentifiers);
        entry.IdempotencyKey.Should().Be(documentNumber);
        entry.TransmittedArtifactHash.Should().StartWith("sha256:");
        entry.PaResponseSnapshot.Should().Contain("accepted");

        // 4) TRACE DE SUPPORT : copie de l'artefact réellement transmis, tenant-scopée.
        trace.Writes.Should().ContainSingle();
        trace.Writes[0].TenantId.Should().Be(SendTestData.TenantSlug);
        trace.Writes[0].DocumentId.Should().Be(id);
        trace.Writes[0].FacturX.Should().Equal(artifact);

        archive.Requests.Should().ContainSingle();
        purge.Calls.Should().ContainSingle();
        runLogs.Saved[^1].DocumentsSucceeded.Should().Be(1);
    }

    [Fact]
    public async Task Rejected_By_Pa_Keeps_Staging_And_Does_Not_Purge()
    {
        var (id, queries, lifecycle, staging) = SeedSingle("ReadyToSend");
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var purge = new SendTestDoubles.RecordingStagingPurgeService(true);
        var archive = new SendTestDoubles.RecordingArchiveService();
        var fake = await PublishedFakeAsync(FakePaScenario.Rejected);
        var provider = BuildProvider(ActiveAccountSettings(), queries, lifecycle, staging, purge, archive, runLogs, fake);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.Rejected.Should().ContainSingle().Which.Should().Be(id);
        purge.Calls.Should().BeEmpty("un rejet conserve le staging (correction/resoumission, jamais archivé en WORM).");
        archive.Requests.Should().BeEmpty();
        runLogs.Saved[^1].DocumentsFailed.Should().Be(1);
    }

    [Fact]
    public async Task Sending_Already_Known_By_Pa_Is_Finalized_Without_Resending()
    {
        var id = Guid.NewGuid();
        const string number = "F-2026-0099";
        var document = SendTestData.Document(id, "Sending", number: number, payloadHash: "hash-99", paDocumentId: "FAKE-" + number);
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        queries.AddDocument(document);
        queries.AddPotentiallySent(SendTestData.Summary(id, "Sending", number));

        var pivot = SendTestData.SingleLinePivot(number);
        var staging = new SendTestDoubles.MapStagingStore();
        staging.Stage(id, CanonicalJson.Serialize(pivot));

        var lifecycle = new SendTestDoubles.RecordingDocumentLifecycle();
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var archive = new SendTestDoubles.RecordingArchiveService();
        var purge = new SendTestDoubles.RecordingStagingPurgeService(true);
        var fake = await PublishedFakeAsync(FakePaScenario.Success);

        // Cycle N : la PA a émis le document (réponse perdue → document resté Sending côté plateforme).
        await fake.SendDocumentAsync(pivot);
        fake.IssuedDocumentNumbers.Should().ContainSingle();
        var sendCallsBefore = fake.Calls.Count(c => c.Method == nameof(IPaClient.SendDocumentAsync));

        var provider = BuildProvider(ActiveAccountSettings(), queries, lifecycle, staging, purge, archive, runLogs, fake);

        // Cycle N+1 : raccrochage anti-doublon.
        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.Issued.Should().ContainSingle().Which.Should().Be(id);
        fake.Calls.Should().Contain(c => c.Method == nameof(IPaClient.GetDocumentStatusAsync), "la PA est interrogée avant tout renvoi.");
        fake.Calls.Count(c => c.Method == nameof(IPaClient.SendDocumentAsync)).Should().Be(sendCallsBefore, "le document déjà connu n'est PAS renvoyé.");
        fake.IssuedDocumentNumbers.Should().ContainSingle("aucune nouvelle émission (anti-doublon).");
        archive.Requests.Should().ContainSingle();
        purge.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Pa_Sending_Response_Persists_Pa_Reference_For_Async_Recovery()
    {
        // AC1 (PIPE01, D7) : une PA asynchrone (Chorus Pro/SuperPDP) accepte le dépôt et renvoie une RÉFÉRENCE
        // de flux (PaSendState.Sending + PaDocumentId). Le pipeline PERSISTE cette référence sur le document
        // RESTÉ Sending : c'est elle qui permet au raccrochage d'interroger la PA et de ne JAMAIS re-déposer
        // (anti double-dépôt). Sans persistance, une PA asynchrone serait re-déposée = double déclaration fiscale.
        var (id, queries, lifecycle, staging) = SeedSingle("ReadyToSend");
        var number = (await queries.GetByIdAsync(id))!.DocumentNumber;
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var purge = new SendTestDoubles.RecordingStagingPurgeService(true);
        var archive = new SendTestDoubles.RecordingArchiveService();
        var pa = new SendTestDoubles.SendingPaClient(); // POST 200 → Sending + PaDocumentId "SPDP-{number}".

        var provider = BuildInlineProvider(queries, lifecycle, staging, purge, archive, runLogs, pa);
        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        var recorded = lifecycle.RecordedPaReferences.Should().ContainSingle().Subject;
        recorded.DocumentId.Should().Be(id);
        recorded.PaDocumentId.Should().Be($"SPDP-{number}", "la référence de flux renvoyée par la PA asynchrone est persistée pour le raccrochage.");
        recorded.PaResponse.Should().Contain("api:uploaded", "la réponse brute de l'accusé de dépôt est conservée pour la piste d'audit.");
        lifecycle.Issued.Should().BeEmpty("un dépôt accepté ≠ émis : pas de finalisation sur le seul POST.");
        lifecycle.Rejected.Should().BeEmpty();
        lifecycle.TechnicalError.Should().BeEmpty();
    }

    [Fact]
    public async Task RecoverSending_Async_Reference_Still_Processing_Is_Never_Redeposited()
    {
        // AC3 (PIPE01) : un document déposé sur une PA asynchrone (PaDocumentId persisté), encore EN TRAITEMENT
        // côté PA (statut Sending) au cycle de raccrochage. Le pipeline RELIT le statut par la référence et
        // MAINTIENT Sending — il ne RE-DÉPOSE JAMAIS le flux (Chorus Pro créerait un nouveau flux = double dépôt).
        var id = Guid.NewGuid();
        const string number = "F-2026-0201";
        var document = SendTestData.Document(id, "Sending", number: number, payloadHash: "hash-201", paDocumentId: "FLUX-" + number);
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        queries.AddDocument(document);
        queries.AddPotentiallySent(SendTestData.Summary(id, "Sending", number));

        var staging = new SendTestDoubles.MapStagingStore();
        staging.Stage(id, CanonicalJson.Serialize(SendTestData.SingleLinePivot(number)));

        var lifecycle = new SendTestDoubles.RecordingDocumentLifecycle();
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var purge = new SendTestDoubles.RecordingStagingPurgeService(true);
        var archive = new SendTestDoubles.RecordingArchiveService();
        var pa = new SendTestDoubles.AsyncReferenceStatusPaClient(PaSendState.Sending);

        var provider = BuildInlineProvider(queries, lifecycle, staging, purge, archive, runLogs, pa);
        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        pa.StatusCount.Should().Be(1, "la PA est interrogée par référence de flux.");
        pa.SendCount.Should().Be(0, "un flux déjà accepté n'est JAMAIS re-déposé (anti double-dépôt).");
        lifecycle.Issued.Should().BeEmpty("statut encore Sending : pas de finalisation.");
        lifecycle.Rejected.Should().BeEmpty();
        lifecycle.TechnicalError.Should().BeEmpty("un raccrochage en attente n'est pas une erreur technique.");
        archive.Requests.Should().BeEmpty();
        purge.Calls.Should().BeEmpty();
        runLogs.Saved[^1].DocumentsFailed.Should().Be(0, "raccrochage différé, pas un échec.");
        runLogs.Saved[^1].DocumentsSucceeded.Should().Be(0);
    }

    [Fact]
    public async Task RecoverSending_Async_Reference_Rejected_Marks_RejectedByPa_Without_Redeposit()
    {
        // AC2 (PIPE01) : un document déposé sur une PA asynchrone, RELU comme REJETÉ au raccrochage → RejectedByPa
        // (staging conservé pour correction/resoumission), SANS aucune re-soumission automatique du flux.
        var id = Guid.NewGuid();
        const string number = "F-2026-0202";
        var document = SendTestData.Document(id, "Sending", number: number, payloadHash: "hash-202", paDocumentId: "FLUX-" + number);
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        queries.AddDocument(document);
        queries.AddPotentiallySent(SendTestData.Summary(id, "Sending", number));

        var staging = new SendTestDoubles.MapStagingStore();
        staging.Stage(id, CanonicalJson.Serialize(SendTestData.SingleLinePivot(number)));

        var lifecycle = new SendTestDoubles.RecordingDocumentLifecycle();
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var purge = new SendTestDoubles.RecordingStagingPurgeService(true);
        var archive = new SendTestDoubles.RecordingArchiveService();
        var pa = new SendTestDoubles.AsyncReferenceStatusPaClient(PaSendState.RejectedByPa);

        var provider = BuildInlineProvider(queries, lifecycle, staging, purge, archive, runLogs, pa);
        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        pa.SendCount.Should().Be(0, "un flux déjà déposé n'est jamais re-soumis, même rejeté (la source corrige puis re-soumet).");
        lifecycle.Rejected.Should().ContainSingle().Which.Should().Be(id);
        lifecycle.Issued.Should().BeEmpty();
        purge.Calls.Should().BeEmpty("un rejet conserve le staging (jamais archivé en WORM).");
        archive.Requests.Should().BeEmpty();
        runLogs.Saved[^1].DocumentsFailed.Should().Be(1);
    }

    [Fact]
    public async Task Corrupt_Staging_Is_Not_Sent_And_Causes_No_Transition()
    {
        var id = Guid.NewGuid();
        var document = SendTestData.Document(id, "ReadyToSend");
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        queries.AddDocument(document);
        queries.AddInState("ReadyToSend", SendTestData.Summary(id, "ReadyToSend"));

        var staging = new SendTestDoubles.MapStagingStore();
        staging.StageIntegrityFailure(id);

        var lifecycle = new SendTestDoubles.RecordingDocumentLifecycle();
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var purge = new SendTestDoubles.RecordingStagingPurgeService(false);
        var archive = new SendTestDoubles.RecordingArchiveService();
        var fake = await PublishedFakeAsync(FakePaScenario.Success);
        var provider = BuildProvider(ActiveAccountSettings(), queries, lifecycle, staging, purge, archive, runLogs, fake);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.BeganSending.Should().BeEmpty("aucun envoi d'un contenu altéré.");
        lifecycle.Issued.Should().BeEmpty("le document n'est pas émis.");
        lifecycle.Blocked.Should().BeEmpty("aucune transition d'état illégale depuis ReadyToSend.");
        fake.IssuedDocumentNumbers.Should().BeEmpty("rien n'est envoyé à la PA.");
        runLogs.Saved.Should().ContainSingle();
        runLogs.Saved[0].DocumentsFailed.Should().Be(1);
    }

    [Fact]
    public async Task More_Than_PageSize_ReadyToSend_Documents_Are_All_Issued_In_One_Run()
    {
        // Arrange : 120 documents ReadyToSend — dépasse la taille de page (100).
        // Avec l'ancienne logique OFFSET-draining, page++ après chaque page ferait sauter les 20 derniers.
        // Avec le snapshot, tous les 120 ids sont collectés avant tout traitement.
        const int documentCount = 120;
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        var lifecycle = new SendTestDoubles.RecordingDocumentLifecycle();
        var staging = new SendTestDoubles.MapStagingStore();

        for (var i = 1; i <= documentCount; i++)
        {
            var id = Guid.NewGuid();
            var number = string.Format(System.Globalization.CultureInfo.InvariantCulture, "F-PAGE-{0:D3}", i);
            var pivot = SendTestData.SingleLinePivot(number);
            var doc = SendTestData.Document(id, "ReadyToSend", number: number, payloadHash: "hash-page-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            queries.AddDocument(doc);
            queries.AddInState("ReadyToSend", SendTestData.Summary(id, "ReadyToSend", number));
            staging.Stage(id, Liakont.Agent.Contracts.Serialization.CanonicalJson.Serialize(pivot));
        }

        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var purge = new SendTestDoubles.RecordingStagingPurgeService(true);
        var archive = new SendTestDoubles.RecordingArchiveService();
        var fake = await PublishedFakeAsync(FakePaScenario.Success);
        var provider = BuildProvider(ActiveAccountSettings(), queries, lifecycle, staging, purge, archive, runLogs, fake);

        // Act
        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        // Assert : tous les 120 documents ont été émis (aucun sauté par OFFSET-draining).
        lifecycle.Issued.Should().HaveCount(documentCount, "le snapshot collecte tous les ids avant traitement — aucun document sauté.");
        runLogs.Saved[^1].DocumentsSucceeded.Should().Be(documentCount);
    }

    private static (Guid Id, SendTestDoubles.ConfigurableDocumentQueries Queries, SendTestDoubles.RecordingDocumentLifecycle Lifecycle, SendTestDoubles.MapStagingStore Staging) SeedSingle(string state)
    {
        var id = Guid.NewGuid();
        var document = SendTestData.Document(id, state);
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        queries.AddDocument(document);
        queries.AddInState(state, SendTestData.Summary(id, state));

        var staging = new SendTestDoubles.MapStagingStore();
        staging.Stage(id, CanonicalJson.Serialize(SendTestData.SingleLinePivot(document.DocumentNumber)));

        return (id, queries, new SendTestDoubles.RecordingDocumentLifecycle(), staging);
    }

    private static SendTestDoubles.ConfigurableTenantSettingsQueries ActiveAccountSettings() =>
        new(TenantCompany, new[] { SendTestData.ActiveAccount() });

    private static async Task<FakePaClient> PublishedFakeAsync(FakePaScenario scenario)
    {
        var fake = new FakePaClient(new FakePaClientOptions { SendScenario = scenario });
        await fake.EnsureTaxReportSettingAsync(new PaTaxReportSettingRequest
        {
            StartDate = new DateOnly(2026, 1, 1),
            TypeOperation = "LBS",
            EnterpriseSize = "PME",
        });
        return fake;
    }

    /// <summary>PA publiée mais ne déclarant PAS la capacité e-reporting B2C (le reste = défaut V1) — pour la garde 10.3 (B2C01).</summary>
    private static async Task<FakePaClient> PublishedFakeWithoutB2cReportingAsync()
    {
        var fake = new FakePaClient(new FakePaClientOptions
        {
            Capabilities = new PaCapabilities
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
            },
        });
        await fake.EnsureTaxReportSettingAsync(new PaTaxReportSettingRequest
        {
            StartDate = new DateOnly(2026, 1, 1),
            TypeOperation = "LBS",
            EnterpriseSize = "PME",
        });
        return fake;
    }

    /// <summary>
    /// Provider inline pour un <see cref="IPaClient"/> ARBITRAIRE (au-delà de <see cref="FakePaClient"/>) :
    /// utilisé par les tests de raccrochage asynchrone (PIPE01) avec <c>AsyncReferenceStatusPaClient</c> et
    /// <c>SendingPaClient</c>. Tenant à compte actif (<see cref="ActiveAccountSettings"/>).
    /// </summary>
    private static SendTestDoubles.FakeServiceProvider BuildInlineProvider(
        SendTestDoubles.ConfigurableDocumentQueries queries,
        SendTestDoubles.RecordingDocumentLifecycle lifecycle,
        SendTestDoubles.MapStagingStore staging,
        SendTestDoubles.RecordingStagingPurgeService purge,
        SendTestDoubles.RecordingArchiveService archive,
        SendTestDoubles.RecordingRunLogStore runLogs,
        IPaClient paClient) =>
        new SendTestDoubles.FakeServiceProvider()
            .Add<TimeProvider>(new SendTestDoubles.FixedTimeProvider(SendTestData.Now))
            .Add<ILogger<SendTenantJob>>(NullLogger<SendTenantJob>.Instance)
            .Add<Liakont.Modules.TenantSettings.Contracts.Queries.ITenantSettingsQueries>(ActiveAccountSettings())
            .Add<IPaClientRegistry>(new SendTestDoubles.StubPaClientRegistry(paClient))
            .Add<Liakont.Modules.Documents.Contracts.Queries.IDocumentQueries>(queries)
            .Add<Liakont.Modules.Documents.Contracts.Lifecycle.IDocumentLifecycle>(lifecycle)
            .Add<Liakont.Modules.Documents.Contracts.Queries.IPaTransmissionJournalQueries>(new SendTestDoubles.StubPaTransmissionJournalQueries())
            .Add<Liakont.Modules.Staging.Contracts.IPayloadStagingStore>(staging)
            .Add<Liakont.Modules.Staging.Contracts.IStagingPurgeService>(purge)
            .Add<Liakont.Modules.Archive.Contracts.IArchiveService>(archive)
            .Add<Liakont.Modules.TvaMapping.Contracts.Services.ITvaMappingService>(SendTestDoubles.FakeTvaMappingService.Mapping())
            .Add<Liakont.Modules.Pipeline.Application.IPipelineRunLogStore>(runLogs);

    private static SendTestDoubles.FakeServiceProvider BuildProvider(
        SendTestDoubles.ConfigurableTenantSettingsQueries tenantSettings,
        SendTestDoubles.ConfigurableDocumentQueries queries,
        SendTestDoubles.RecordingDocumentLifecycle lifecycle,
        SendTestDoubles.MapStagingStore staging,
        SendTestDoubles.RecordingStagingPurgeService purge,
        SendTestDoubles.RecordingArchiveService archive,
        SendTestDoubles.RecordingRunLogStore runLogs,
        FakePaClient paClient,
        SendTestDoubles.StubPaTransmissionJournalQueries? journalQueries = null,
        Liakont.Modules.TvaMapping.Contracts.Services.ITvaMappingService? tvaMapping = null)
    {
        return new SendTestDoubles.FakeServiceProvider()
            .Add<TimeProvider>(new SendTestDoubles.FixedTimeProvider(SendTestData.Now))
            .Add<ILogger<SendTenantJob>>(NullLogger<SendTenantJob>.Instance)
            .Add<Liakont.Modules.TenantSettings.Contracts.Queries.ITenantSettingsQueries>(tenantSettings)
            .Add<IPaClientRegistry>(new SendTestDoubles.StubPaClientRegistry(paClient))
            .Add<Liakont.Modules.Documents.Contracts.Queries.IDocumentQueries>(queries)
            .Add<Liakont.Modules.Documents.Contracts.Lifecycle.IDocumentLifecycle>(lifecycle)
            .Add<Liakont.Modules.Documents.Contracts.Queries.IPaTransmissionJournalQueries>(journalQueries ?? new SendTestDoubles.StubPaTransmissionJournalQueries())
            .Add<Liakont.Modules.Staging.Contracts.IPayloadStagingStore>(staging)
            .Add<Liakont.Modules.Staging.Contracts.IStagingPurgeService>(purge)
            .Add<Liakont.Modules.Archive.Contracts.IArchiveService>(archive)
            .Add<Liakont.Modules.TvaMapping.Contracts.Services.ITvaMappingService>(tvaMapping ?? SendTestDoubles.FakeTvaMappingService.Mapping())
            .Add<Liakont.Modules.Pipeline.Application.IPipelineRunLogStore>(runLogs);
    }

    /// <summary>
    /// Provider du chemin Factur-X (FX07) : comme <see cref="BuildProvider"/> mais avec une PA à capacité
    /// <c>SupportsFacturXTransmission</c> et les services de génération / journalisation / trace de support.
    /// </summary>
    private static SendTestDoubles.FakeServiceProvider BuildFacturXProvider(
        SendTestDoubles.ConfigurableTenantSettingsQueries tenantSettings,
        SendTestDoubles.ConfigurableDocumentQueries queries,
        SendTestDoubles.RecordingDocumentLifecycle lifecycle,
        SendTestDoubles.MapStagingStore staging,
        SendTestDoubles.RecordingStagingPurgeService purge,
        SendTestDoubles.RecordingArchiveService archive,
        SendTestDoubles.RecordingRunLogStore runLogs,
        SendTestDoubles.FacturXCapablePaClient paClient,
        SendTestDoubles.StubFacturXArtifactBuilder builder,
        SendTestDoubles.RecordingPaTransmissionJournal journal,
        SendTestDoubles.RecordingSupportTraceStore trace,
        SendTestDoubles.StubPaTransmissionJournalQueries? journalQueries = null)
    {
        return new SendTestDoubles.FakeServiceProvider()
            .Add<TimeProvider>(new SendTestDoubles.FixedTimeProvider(SendTestData.Now))
            .Add<ILogger<SendTenantJob>>(NullLogger<SendTenantJob>.Instance)
            .Add<Liakont.Modules.TenantSettings.Contracts.Queries.ITenantSettingsQueries>(tenantSettings)
            .Add<IPaClientRegistry>(new SendTestDoubles.StubPaClientRegistry(paClient))
            .Add<Liakont.Modules.Documents.Contracts.Queries.IDocumentQueries>(queries)
            .Add<Liakont.Modules.Documents.Contracts.Lifecycle.IDocumentLifecycle>(lifecycle)
            .Add<Liakont.Modules.Documents.Contracts.Lifecycle.IPaTransmissionJournal>(journal)
            .Add<Liakont.Modules.Documents.Contracts.Queries.IPaTransmissionJournalQueries>(journalQueries ?? new SendTestDoubles.StubPaTransmissionJournalQueries())
            .Add<Liakont.Modules.Staging.Contracts.IPayloadStagingStore>(staging)
            .Add<Liakont.Modules.Staging.Contracts.IStagingPurgeService>(purge)
            .Add<Liakont.Modules.Archive.Contracts.IArchiveService>(archive)
            .Add<Liakont.Modules.SupportTrace.Contracts.ISupportTraceStore>(trace)
            .Add<Liakont.Modules.Transmission.Contracts.IFacturXArtifactBuilder>(builder)
            .Add<Liakont.Modules.TvaMapping.Contracts.Services.ITvaMappingService>(SendTestDoubles.FakeTvaMappingService.Mapping())
            .Add<Liakont.Modules.Pipeline.Application.IPipelineRunLogStore>(runLogs);
    }

    [Fact]
    public async Task RecoverSending_With_FacturX_Pa_And_No_Prior_Journal_Generates_Transmits_And_Journals()
    {
        // Chemin de reprise (recover Sending) sur une PA Essentiel SANS journal préexistant : on doit générer,
        // transmettre et journaliser (INV-PIPELINE-038 sur le chemin de reprise). La garde ne doit PAS
        // déclencher (journal vide = FindByIdempotencyKey → null).
        var id = Guid.NewGuid();
        const string number = "F-2026-0150";
        var document = SendTestData.Document(id, "Sending", number: number, payloadHash: "hash-150");
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        queries.AddDocument(document);
        queries.AddPotentiallySent(SendTestData.Summary(id, "Sending", number));

        var pivot = SendTestData.SingleLinePivot(number);
        var staging = new SendTestDoubles.MapStagingStore();
        staging.Stage(id, Liakont.Agent.Contracts.Serialization.CanonicalJson.Serialize(pivot));

        var lifecycle = new SendTestDoubles.RecordingDocumentLifecycle();
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var purge = new SendTestDoubles.RecordingStagingPurgeService(true);
        var archive = new SendTestDoubles.RecordingArchiveService();
        var pa = new SendTestDoubles.FacturXCapablePaClient();
        var artifact = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 recover-no-journal");
        var builder = new SendTestDoubles.StubFacturXArtifactBuilder(artifact);
        var journal = new SendTestDoubles.RecordingPaTransmissionJournal();
        var trace = new SendTestDoubles.RecordingSupportTraceStore();
        var journalQueries = new SendTestDoubles.StubPaTransmissionJournalQueries();

        var account = SendTestData.ActiveAccount("generique");
        var provider = BuildFacturXProvider(
            new SendTestDoubles.ConfigurableTenantSettingsQueries(TenantCompany, new[] { account }),
            queries, lifecycle, staging, purge, archive, runLogs, pa, builder, journal, trace,
            journalQueries);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        builder.BuildCount.Should().Be(1, "le Factur-X est généré sur le chemin de reprise (INV-PIPELINE-038).");
        pa.SendCount.Should().Be(1, "aucun journal préexistant : la garde ne déclenche pas, on transmet.");
        journal.Entries.Should().ContainSingle("la journalisation a lieu après la transmission réussie.");
        lifecycle.Issued.Should().ContainSingle().Which.Should().Be(id);
    }

    [Fact]
    public async Task RecoverSending_With_Already_Journaled_FacturX_Does_Not_Retransmit()
    {
        // Garde anti double-envoi (INV-PIPELINE-041) : un cycle précédent a transmis et journalisé (FX07) mais
        // crashé AVANT MarkIssued. Le document est resté Sending. Au cycle suivant, la garde lit la clé
        // d'idempotence et finalise Issued SANS retransmettre (jamais de double émission).
        var id = Guid.NewGuid();
        const string number = "F-2026-0160";
        var document = SendTestData.Document(id, "Sending", number: number, payloadHash: "hash-160");
        var queries = new SendTestDoubles.ConfigurableDocumentQueries();
        queries.AddDocument(document);
        queries.AddPotentiallySent(SendTestData.Summary(id, "Sending", number));

        var pivot = SendTestData.SingleLinePivot(number);
        var staging = new SendTestDoubles.MapStagingStore();
        staging.Stage(id, Liakont.Agent.Contracts.Serialization.CanonicalJson.Serialize(pivot));

        var lifecycle = new SendTestDoubles.RecordingDocumentLifecycle();
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var purge = new SendTestDoubles.RecordingStagingPurgeService(true);
        var archive = new SendTestDoubles.RecordingArchiveService();
        var pa = new SendTestDoubles.FacturXCapablePaClient();
        var artifact = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 recover-already-journaled");
        var builder = new SendTestDoubles.StubFacturXArtifactBuilder(artifact);
        var journal = new SendTestDoubles.RecordingPaTransmissionJournal();
        var trace = new SendTestDoubles.RecordingSupportTraceStore();

        // La garde lit ce journal : la clé = numéro de document, déjà journalisée.
        var journalQueries = new SendTestDoubles.StubPaTransmissionJournalQueries();
        journalQueries.AddJournaled(number, id);

        var account = SendTestData.ActiveAccount("generique");
        var provider = BuildFacturXProvider(
            new SendTestDoubles.ConfigurableTenantSettingsQueries(TenantCompany, new[] { account }),
            queries, lifecycle, staging, purge, archive, runLogs, pa, builder, journal, trace,
            journalQueries);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        pa.SendCount.Should().Be(0, "déjà transmis : aucune retransmission.");
        builder.BuildCount.Should().Be(0, "pas de régénération.");
        lifecycle.Issued.Should().ContainSingle().Which.Should().Be(id, "finalisé via le raccrochage.");
        journal.Entries.Should().BeEmpty("aucune nouvelle journalisation : facturX null sur le raccrochage.");
        archive.Requests.Should().ContainSingle("la preuve WORM est posée même sur le raccrochage.");
    }

    [Fact]
    public async Task Pilotage_Path_Does_Not_Journal_Nor_Trace()
    {
        // INV-PIPELINE-040 : sur le chemin Pilotage (PA sans SupportsFacturXTransmission), ni journal ni trace
        // ne doivent être écrits. La garde anti double-envoi (TryFinalizeFromJournalAsync) est no-op (journal
        // vide → retourne false), et FinalizeIssuedAsync ne journalise pas (facturX null).
        var (id, queries, lifecycle, staging) = SeedSingle("ReadyToSend");
        var runLogs = new SendTestDoubles.RecordingRunLogStore();
        var purge = new SendTestDoubles.RecordingStagingPurgeService(wormPresent: true);
        var archive = new SendTestDoubles.RecordingArchiveService();
        var journal = new SendTestDoubles.RecordingPaTransmissionJournal();
        var trace = new SendTestDoubles.RecordingSupportTraceStore();
        var journalQueries = new SendTestDoubles.StubPaTransmissionJournalQueries();

        // PA Pilotage : SupportsFacturXTransmission == false (FakePaClient avec tax_report_setting publié).
        var fake = await PublishedFakeAsync(FakePaScenario.Success);

        var provider = new SendTestDoubles.FakeServiceProvider()
            .Add<TimeProvider>(new SendTestDoubles.FixedTimeProvider(SendTestData.Now))
            .Add<ILogger<SendTenantJob>>(NullLogger<SendTenantJob>.Instance)
            .Add<Liakont.Modules.TenantSettings.Contracts.Queries.ITenantSettingsQueries>(ActiveAccountSettings())
            .Add<IPaClientRegistry>(new SendTestDoubles.StubPaClientRegistry(fake))
            .Add<Liakont.Modules.Documents.Contracts.Queries.IDocumentQueries>(queries)
            .Add<Liakont.Modules.Documents.Contracts.Lifecycle.IDocumentLifecycle>(lifecycle)
            .Add<Liakont.Modules.Documents.Contracts.Lifecycle.IPaTransmissionJournal>(journal)
            .Add<Liakont.Modules.Documents.Contracts.Queries.IPaTransmissionJournalQueries>(journalQueries)
            .Add<Liakont.Modules.Staging.Contracts.IPayloadStagingStore>(staging)
            .Add<Liakont.Modules.Staging.Contracts.IStagingPurgeService>(purge)
            .Add<Liakont.Modules.Archive.Contracts.IArchiveService>(archive)
            .Add<Liakont.Modules.TvaMapping.Contracts.Services.ITvaMappingService>(SendTestDoubles.FakeTvaMappingService.Mapping())
            .Add<Liakont.Modules.Pipeline.Application.IPipelineRunLogStore>(runLogs);

        await new SendTenantJob().ExecuteAsync(new TenantJobContext(SendTestData.TenantSlug, provider));

        lifecycle.Issued.Should().ContainSingle().Which.Should().Be(id, "le Pilotage émet normalement.");
        journal.Entries.Should().BeEmpty("le chemin Pilotage NE journalise PAS (INV-PIPELINE-040).");
        trace.Writes.Should().BeEmpty("le chemin Pilotage NE trace PAS (INV-PIPELINE-040).");
    }
}
