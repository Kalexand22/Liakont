namespace Liakont.PaClients.Contract.Tests;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Suite de contrat COMMUNE que TOUT plug-in PA doit hériter et passer (blueprint.md §2 règle 3 ;
/// testing-strategy §6 ; F05). Elle vérifie l'INVARIANT central du produit — « le produit n'est
/// JAMAIS bloqué par les limites d'un PA » : une capacité absente retourne un résultat TYPÉ, jamais
/// une exception (PAA01). Le comportement attendu est piloté par les capacités DÉCLARÉES
/// (<see cref="PaCapabilities"/>), jamais par un <c>if (pa is …)</c> (CLAUDE.md n°8/16).
///
/// <para>
/// La suite n'asserte QUE la surface observable d'<see cref="IPaClient"/> (les DTOs retournés + les
/// capacités déclarées + l'absence d'exception). La vérification du format « fil » (payload réellement
/// envoyé à la PA) est propre à chaque plug-in et reste dans ses tests dédiés (le Fake inspecte son
/// journal d'appels ; B2Brouter / Super PDP inspectent le corps HTTP du mock).
/// </para>
///
/// <para>
/// Un plug-in fournit sa suite réelle en héritant cette classe et en implémentant
/// <see cref="CreateClient"/> : il sait produire son client dans chaque issue d'un PA (succès / rejet /
/// erreur silencieuse / timeout) et avec des capacités données. La suite tourne ici contre
/// <c>FakePaClient</c> (<see cref="FakePaClientContractTests"/>) — preuve qu'elle est exécutable (PAA03).
/// </para>
/// </summary>
public abstract class PaClientContractTests
{
    /// <summary>
    /// Fabrique le client du plug-in testé, configuré selon <paramref name="setup"/> (issue appliquée
    /// aux envois et, le cas échéant, capacités déclarées). Implémenté par chaque plug-in : Fake en
    /// mémoire, mock HTTP pour les PA réelles.
    /// </summary>
    /// <param name="setup">Configuration du cas de contrat à exercer.</param>
    protected abstract IPaClient CreateClient(PaClientContractSetup setup);

    // ── Contrat ──

    /// <summary>Un envoi valide accepté par la PA produit un résultat exploitable (Issued + identifiant).</summary>
    [Fact]
    public async Task Send_Valid_Document_Yields_A_Usable_Issued_Result()
    {
        var client = CreateClient(new PaClientContractSetup());

        var result = await client.SendDocumentAsync(Invoice("CT-1"));

        result.State.Should().Be(PaSendState.Issued);
        result.PaDocumentId.Should().NotBeNullOrWhiteSpace(
            "un envoi abouti porte l'identifiant attribué par la PA (F05 §3)");
    }

    /// <summary>
    /// Un avoir (porteur de la référence de sa facture d'origine) est accepté si la PA déclare la
    /// capacité ; sinon il dégrade en résultat typé, jamais une exception (PAA01).
    /// </summary>
    [Fact]
    public async Task Send_CreditNote_Follows_The_Declared_Capability()
    {
        var client = CreateClient(new PaClientContractSetup());

        // L'avoir porte la référence de sa facture d'origine (lien transmis AU plug-in) ; le format
        // « fil » est vérifié par les tests propres du plug-in, pas au niveau du contrat observable.
        var result = await client.SendDocumentAsync(CreditNote("CT-AV-1"));

        if (client.Capabilities.SupportsCreditNotes)
        {
            result.State.Should().Be(PaSendState.Issued, "un avoir supporté est accepté");
        }
        else
        {
            result.State.Should().Be(
                PaSendState.CapabilityNotSupported,
                "un avoir non supporté ne bloque jamais le produit : résultat typé (PAA01)");
            result.CapabilityNotSupported!.Capability.Should().Be(PaCapability.CreditNotes);
        }
    }

    /// <summary>Un rejet métier remonte les erreurs de la PA intactes et n'émet rien (F05 §3).</summary>
    [Fact]
    public async Task Rejected_Document_Surfaces_Errors_Intact_And_Is_Not_Issued()
    {
        var errors = new[] { new PaError("CT_E1", "Rejet métier simulé pour le contrat.") };
        var client = CreateClient(new PaClientContractSetup
        {
            Outcome = PaSendOutcome.Rejected,
            RejectionErrors = errors,
        });

        var result = await client.SendDocumentAsync(Invoice("CT-2"));

        result.State.Should().Be(PaSendState.RejectedByPa);
        result.Errors.Should().NotBeEmpty("les erreurs de la PA remontent intactes (F05 §3)");
        result.PaDocumentId.Should().BeNull("un rejet n'est pas une émission");
    }

    /// <summary>
    /// Une erreur silencieuse (succès au niveau transport mais errors[] non vide) est DÉTECTÉE comme un
    /// rejet, jamais prise pour une émission (F05 §4.1) — robustesse fiscale (CLAUDE.md n°3).
    /// </summary>
    [Fact]
    public async Task Silent_Error_Is_Detected_As_Rejected_Despite_Transport_Success()
    {
        var client = CreateClient(new PaClientContractSetup { Outcome = PaSendOutcome.SilentError });

        var result = await client.SendDocumentAsync(Invoice("CT-3"));

        result.State.Should().Be(
            PaSendState.RejectedByPa,
            "une erreur silencieuse (succès transport + errors[]) est un rejet, pas une émission (F05 §4.1)");
        result.Errors.Should().NotBeEmpty();
    }

    /// <summary>Réseau / 5xx / timeout dégradent en erreur technique re-tentable (F05 §4.1).</summary>
    /// <param name="outcome">Issue technique à exercer (erreur technique ou timeout).</param>
    [Theory]
    [InlineData(PaSendOutcome.TechnicalError)]
    [InlineData(PaSendOutcome.Timeout)]
    public async Task Technical_And_Timeout_Errors_Are_Retryable(PaSendOutcome outcome)
    {
        var client = CreateClient(new PaClientContractSetup { Outcome = outcome });

        var result = await client.SendDocumentAsync(Invoice("CT-4"));

        result.State.Should().Be(
            PaSendState.TechnicalError,
            "réseau / 5xx / timeout sont re-tentables au prochain run (F05 §4.1)");
    }

    /// <summary>Le même numéro de document, envoyé deux fois, n'est jamais émis deux fois (idempotence F05).</summary>
    [Fact]
    public async Task Same_Document_Number_Is_Idempotent_Never_Sent_Twice()
    {
        var client = CreateClient(new PaClientContractSetup());

        var first = await client.SendDocumentAsync(Invoice("CT-DUP"));
        var second = await client.SendDocumentAsync(Invoice("CT-DUP"));

        first.State.Should().Be(PaSendState.Issued);
        second.State.Should().Be(PaSendState.Issued);
        second.PaDocumentId.Should().Be(
            first.PaDocumentId,
            "le même numéro ne produit jamais une seconde émission (idempotence F05)");
    }

    /// <summary>
    /// Une capacité retirée des capacités déclarées dégrade en résultat TYPÉ et journalisable, jamais
    /// une exception ni un blocage du produit — l'invariant central de l'abstraction (PAA01).
    /// </summary>
    [Fact]
    public async Task Missing_Capability_Returns_A_Typed_Result_Never_Throws()
    {
        // On part des capacités nominales du plug-in, puis on en retire deux : le produit doit
        // dégrader en résultat typé sur les appels concernés (jamais lever, jamais bloquer).
        var nominal = CreateClient(new PaClientContractSetup()).Capabilities;
        var restricted = nominal with { SupportsCreditNotes = false, SupportsDocumentRetrieval = false };
        var client = CreateClient(new PaClientContractSetup { Capabilities = restricted });

        var send = await client.SendDocumentAsync(CreditNote("CT-AV-2"));
        send.State.Should().Be(PaSendState.CapabilityNotSupported);
        send.CapabilityNotSupported!.Capability.Should().Be(PaCapability.CreditNotes);
        send.CapabilityNotSupported.OperatorMessage.Should().NotBeNullOrWhiteSpace(
            "le message opérateur (français) est journalisable (CLAUDE.md n°12)");

        var generated = await client.GetGeneratedDocumentAsync("CT-ANY");
        generated.Content.Should().BeNull();
        generated.CapabilityNotSupported!.Capability.Should().Be(PaCapability.DocumentRetrieval);
    }

    /// <summary>
    /// Les capacités DÉCLARÉES par le plug-in sont cohérentes avec son comportement réel : ce qui est
    /// déclaré supporté n'est jamais refusé pour « capacité absente », et ce qui n'est pas déclaré
    /// dégrade systématiquement en résultat typé (jamais d'exception) — cœur de l'indépendance produit.
    /// </summary>
    [Fact]
    public async Task Declared_Capabilities_Are_Consistent_With_Real_Behavior()
    {
        var client = CreateClient(new PaClientContractSetup());
        var caps = client.Capabilities;

        // Avoir : émis si déclaré, résultat typé sinon.
        var creditNote = await client.SendDocumentAsync(CreditNote("CT-CAP-1"));
        creditNote.State.Should().Be(
            caps.SupportsCreditNotes ? PaSendState.Issued : PaSendState.CapabilityNotSupported,
            "le comportement de l'avoir suit la capacité déclarée SupportsCreditNotes");

        // Téléchargement de la facture générée : contenu si déclaré, résultat typé sinon (TRK05).
        var generated = await client.GetGeneratedDocumentAsync("CT-CAP-2");
        if (caps.SupportsDocumentRetrieval)
        {
            generated.Content.Should().NotBeNull("la capacité DocumentRetrieval est déclarée");
            generated.CapabilityNotSupported.Should().BeNull();
        }
        else
        {
            generated.CapabilityNotSupported.Should().NotBeNull(
                "sans la capacité DocumentRetrieval, le résultat est typé (jamais d'exception)");
        }

        // E-reporting de paiement : deux capacités SÉPARÉES (domestique 10.4 / international 10.2).
        await AssertPaymentFluxMatchesCapability(
            client, PaymentReportFlux.Domestic, caps.SupportsDomesticPaymentReporting);
        await AssertPaymentFluxMatchesCapability(
            client, PaymentReportFlux.International, caps.SupportsInternationalPaymentReporting);
    }

    private static async Task AssertPaymentFluxMatchesCapability(
        IPaClient client, PaymentReportFlux flux, bool declared)
    {
        var period = new PaymentReportPeriod
        {
            Flux = flux,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 1, 31),
        };

        var result = await client.SendPaymentReportAsync(period);

        if (declared)
        {
            result.State.Should().NotBe(
                PaSendState.CapabilityNotSupported,
                $"le flux de paiement {flux} est déclaré supporté");
        }
        else
        {
            result.State.Should().Be(
                PaSendState.CapabilityNotSupported,
                $"le flux de paiement {flux} non déclaré dégrade en résultat typé (jamais d'exception)");
        }
    }

    // ── Documents pivot de test (montants en decimal — CLAUDE.md n°1 ; données fictives — n°7) ──

    /// <summary>Facture de vente simple identifiée par son numéro (BT-1).</summary>
    private static PivotDocumentDto Invoice(string number) => new(
        sourceDocumentKind: "FACTURE",
        number: number,
        issueDate: new DateTime(2026, 1, 15),
        sourceReference: $"SRC-{number}",
        supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
        totals: new PivotTotalsDto(100m, 20m, 120m),
        operationCategory: OperationCategory.LivraisonBiens);

    /// <summary>Avoir rattaché à une facture d'origine (porte une <see cref="PivotDocumentRefDto"/>).</summary>
    private static PivotDocumentDto CreditNote(string number) => new(
        sourceDocumentKind: "AVOIR",
        number: number,
        issueDate: new DateTime(2026, 2, 1),
        sourceReference: $"SRC-{number}",
        supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
        totals: new PivotTotalsDto(-50m, -10m, -60m),
        operationCategory: OperationCategory.LivraisonBiens,
        creditNoteRefs: [new PivotDocumentRefDto("F-ORIGINE", new DateTime(2026, 1, 10))]);
}
