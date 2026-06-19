namespace Liakont.PaClients.Generique.Tests.Unit;

using System.Text;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Comportement du plug-in générique (F16 §6) : il TRANSPORTE un Factur-X pré-construit (jamais ne le
/// régénère), bloque proprement en l'absence d'artefact, et dégrade toute capacité absente en résultat
/// TYPÉ (jamais d'exception — PAA01). Note : le plug-in n'hérite PAS de <c>PaClientContractTests</c> (qui
/// cible les PA construisant un payload) — c'est une PA « Essentiel » qui ne fait que transmettre.
/// <para>
/// Depuis FX07, <c>SendDocumentAsync</c> route via le <c>PaSendContext</c> étendu (F16 §6.1) : artefact
/// pré-construit présent → transmission réelle (déléguée à <c>TransmitFacturXAsync</c>) ; absent → blocage
/// (jamais d'envoi à vide). <c>TransmitFacturXAsync</c> reste la voie de transmission réelle, exercée ici en
/// unitaire et consommée par le câblage pipeline (génération à l'étape Sending + passage de l'artefact).
/// </para>
/// </summary>
public sealed class GeneriqueClientTests
{
    private static readonly byte[] SampleFacturX = Encoding.ASCII.GetBytes("%PDF-1.7 factur-x");

    private static GeneriqueClient EmailClient(RecordingDeliveryChannel channel) =>
        new(channel, new GeneriqueAccountConfig { Method = DocumentDeliveryMethod.Email, Target = "pa@tenant.test" });

    [Fact]
    public void Capabilities_Declare_Only_FacturXTransmission()
    {
        var client = EmailClient(new RecordingDeliveryChannel(DocumentDeliveryMethod.Email));

        var caps = client.Capabilities;
        caps.SupportsFacturXTransmission.Should().BeTrue();
        caps.PaName.Should().Be("Générique");
        caps.SupportsB2cReporting.Should().BeFalse();
        caps.SupportsDomesticPaymentReporting.Should().BeFalse();
        caps.SupportsB2bInvoicing.Should().BeFalse();
        caps.SupportsCreditNotes.Should().BeFalse();
        caps.SupportsTaxReportRetrieval.Should().BeFalse();
        caps.SupportsDocumentRetrieval.Should().BeFalse();
        caps.SupportsReportRectification.Should().BeFalse();
        caps.SupportsMarginAmountReporting.Should().BeFalse();
    }

    [Fact]
    public void Constructor_Rejects_Channel_Method_Mismatch()
    {
        var fileChannel = new RecordingDeliveryChannel(DocumentDeliveryMethod.FileDeposit);

        var act = () => new GeneriqueClient(
            fileChannel,
            new GeneriqueAccountConfig { Method = DocumentDeliveryMethod.Email, Target = "pa@tenant.test" });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task SendDocumentAsync_Without_Context_Blocks()
    {
        var channel = new RecordingDeliveryChannel(DocumentDeliveryMethod.Email);
        var client = EmailClient(channel);

        // Aucun contexte d'envoi (donc aucun artefact pré-construit) → blocage re-tentable, JAMAIS Issued :
        // la PA générique exige l'artefact fourni par le pipeline (FX07), jamais régénéré (CLAUDE.md n°3/6).
        var result = await client.SendDocumentAsync(TestDocuments.Invoice("F-2026-001"));

        result.State.Should().Be(PaSendState.TechnicalError);
        result.Errors.Should().ContainSingle(e => e.Code == "FXG_ARTEFACT_REQUIS");
        channel.DeliverCount.Should().Be(0, "aucune transmission sans artefact (jamais d'envoi à vide)");
    }

    [Fact]
    public async Task SendDocumentAsync_With_Empty_Artifact_Context_Blocks()
    {
        var channel = new RecordingDeliveryChannel(DocumentDeliveryMethod.Email);
        var client = EmailClient(channel);

        // Contexte présent mais artefact vide → même garde-fou : on bloque plutôt que d'émettre à vide.
        var result = await client.SendDocumentAsync(
            TestDocuments.Invoice("F-2026-001b"),
            context: new PaSendContext(ReadOnlyMemory<byte>.Empty));

        result.State.Should().Be(PaSendState.TechnicalError);
        result.Errors.Should().ContainSingle(e => e.Code == "FXG_ARTEFACT_REQUIS");
        channel.DeliverCount.Should().Be(0);
    }

    [Fact]
    public async Task SendDocumentAsync_With_PreBuilt_Artifact_Transmits()
    {
        var channel = new RecordingDeliveryChannel(DocumentDeliveryMethod.Email);
        var client = EmailClient(channel);

        // FX07 : l'artefact pré-construit est porté par le PaSendContext → SendDocumentAsync transmet
        // (route vers TransmitFacturXAsync), exactement l'artefact reçu, sans jamais le régénérer.
        var result = await client.SendDocumentAsync(
            TestDocuments.Invoice("F-2026-001c"),
            context: new PaSendContext(SampleFacturX));

        result.State.Should().Be(PaSendState.Issued);
        result.PaDocumentId.Should().Be("GENERIQUE-F-2026-001c");
        channel.DeliverCount.Should().Be(1);
        channel.LastRequest!.Content.ToArray().Should().Equal(SampleFacturX);
    }

    [Fact]
    public async Task TransmitFacturXAsync_Transmits_The_Provided_Artifact_Unchanged()
    {
        var channel = new RecordingDeliveryChannel(DocumentDeliveryMethod.Email);
        var client = EmailClient(channel);

        var result = await client.TransmitFacturXAsync("F-2026-002", SampleFacturX);

        result.State.Should().Be(PaSendState.Issued);
        result.PaDocumentId.Should().Be("GENERIQUE-F-2026-002");
        channel.DeliverCount.Should().Be(1);

        var request = channel.LastRequest!;
        request.Method.Should().Be(DocumentDeliveryMethod.Email);
        request.Target.Should().Be("pa@tenant.test");
        request.FileName.Should().Be("factur-x_F-2026-002.pdf");

        // L'artefact transmis est EXACTEMENT celui reçu — le plug-in ne régénère jamais (CLAUDE.md n°6).
        request.Content.ToArray().Should().Equal(SampleFacturX);
        request.Subject.Should().Contain("F-2026-002");
        request.Body.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TransmitFacturXAsync_For_FileDeposit_Has_No_Email_Fields()
    {
        var channel = new RecordingDeliveryChannel(DocumentDeliveryMethod.FileDeposit);
        var client = new GeneriqueClient(
            channel,
            new GeneriqueAccountConfig { Method = DocumentDeliveryMethod.FileDeposit, Target = "/depot/tenant" });

        await client.TransmitFacturXAsync("F-2026-003", SampleFacturX);

        var request = channel.LastRequest!;
        request.Method.Should().Be(DocumentDeliveryMethod.FileDeposit);
        request.Target.Should().Be("/depot/tenant");
        request.Subject.Should().BeNull();
        request.Body.Should().BeNull();
    }

    [Fact]
    public async Task TransmitFacturXAsync_Blocks_On_Empty_Artifact()
    {
        var channel = new RecordingDeliveryChannel(DocumentDeliveryMethod.Email);
        var client = EmailClient(channel);

        var result = await client.TransmitFacturXAsync("F-2026-004", ReadOnlyMemory<byte>.Empty);

        result.State.Should().Be(PaSendState.TechnicalError);
        result.Errors.Should().ContainSingle(e => e.Code == "FXG_ARTEFACT_REQUIS");
        channel.DeliverCount.Should().Be(0);
    }

    [Fact]
    public async Task TransmitFacturXAsync_Propagates_Transport_Failure_Rather_Than_Faking_Success()
    {
        var channel = new RecordingDeliveryChannel(DocumentDeliveryMethod.Email, throwOnDeliver: true);
        var client = EmailClient(channel);

        // Bloquer plutôt qu'émettre faux : un échec de transport remonte, jamais un faux « Issued » (CLAUDE.md n°3).
        var act = async () => await client.TransmitFacturXAsync("F-2026-005", SampleFacturX);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Unsupported_Capabilities_Degrade_To_Typed_Results_Never_Throw()
    {
        var client = EmailClient(new RecordingDeliveryChannel(DocumentDeliveryMethod.Email));

        var payment = await client.SendPaymentReportAsync(new PaymentReportPeriod
        {
            Flux = PaymentReportFlux.Domestic,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 1, 31),
        });
        payment.State.Should().Be(PaSendState.CapabilityNotSupported);
        payment.CapabilityNotSupported!.Capability.Should().Be(PaCapability.DomesticPaymentReporting);

        var generated = await client.GetGeneratedDocumentAsync("GENERIQUE-F-2026-002");
        generated.CapabilityNotSupported!.Capability.Should().Be(PaCapability.DocumentRetrieval);
        generated.Content.Should().BeNull();

        (await client.ListTaxReportsAsync()).Should().BeEmpty();

        var status = await client.GetDocumentStatusAsync("GENERIQUE-F-2026-002");
        status.State.Should().Be(PaSendState.CapabilityNotSupported);

        var ensure = async () => await client.EnsureTaxReportSettingAsync(new PaTaxReportSettingRequest
        {
            StartDate = new DateOnly(2026, 1, 1),
            TypeOperation = "VENTE",
            EnterpriseSize = "PME",
        });
        await ensure.Should().NotThrowAsync();
    }
}
