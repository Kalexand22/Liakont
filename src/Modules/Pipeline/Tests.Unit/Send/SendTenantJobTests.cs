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

    private static SendTestDoubles.FakeServiceProvider BuildProvider(
        SendTestDoubles.ConfigurableTenantSettingsQueries tenantSettings,
        SendTestDoubles.ConfigurableDocumentQueries queries,
        SendTestDoubles.RecordingDocumentLifecycle lifecycle,
        SendTestDoubles.MapStagingStore staging,
        SendTestDoubles.RecordingStagingPurgeService purge,
        SendTestDoubles.RecordingArchiveService archive,
        SendTestDoubles.RecordingRunLogStore runLogs,
        FakePaClient paClient)
    {
        return new SendTestDoubles.FakeServiceProvider()
            .Add<TimeProvider>(new SendTestDoubles.FixedTimeProvider(SendTestData.Now))
            .Add<ILogger<SendTenantJob>>(NullLogger<SendTenantJob>.Instance)
            .Add<Liakont.Modules.TenantSettings.Contracts.Queries.ITenantSettingsQueries>(tenantSettings)
            .Add<IPaClientRegistry>(new SendTestDoubles.StubPaClientRegistry(paClient))
            .Add<Liakont.Modules.Documents.Contracts.Queries.IDocumentQueries>(queries)
            .Add<Liakont.Modules.Documents.Contracts.Lifecycle.IDocumentLifecycle>(lifecycle)
            .Add<Liakont.Modules.Staging.Contracts.IPayloadStagingStore>(staging)
            .Add<Liakont.Modules.Staging.Contracts.IStagingPurgeService>(purge)
            .Add<Liakont.Modules.Archive.Contracts.IArchiveService>(archive)
            .Add<Liakont.Modules.Pipeline.Application.IPipelineRunLogStore>(runLogs);
    }
}
