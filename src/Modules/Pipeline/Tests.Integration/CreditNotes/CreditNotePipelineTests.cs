namespace Liakont.Modules.Pipeline.Tests.Integration.CreditNotes;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Pipeline.Tests.Integration.Check;
using Liakont.Modules.Pipeline.Tests.Integration.EndToEnd;
using Xunit;

/// <summary>
/// PIP02 — Pipeline des avoirs (F07-F08 §B.5), de bout en bout sur une base tenant PostgreSQL réelle
/// (database-per-tenant) : <c>ingestion → CHECK → SEND (Fake)</c>. Couvre l'ORDRE CHRONOLOGIQUE (un avoir
/// n'est émis qu'après sa facture d'origine), le RÉORDONNANCEMENT dans le même traitement (avoir reçu avant
/// sa facture, débloqué dès qu'elle est émise), l'avoir ORPHELIN (bloqué, jamais émis, aucune référence
/// fabriquée) et l'avoir GROUPÉ (émis seulement quand TOUTES ses origines sont émises). Le cas « PA sans
/// capacité avoirs » est couvert par <c>SendTenantJobIntegrationTests</c> (INV-PIPELINE-021).
/// </summary>
/// <remarks>
/// Une base (un conteneur) par MÉTHODE de test (xUnit instancie la classe par test) : aucune pollution
/// inter-test, et la réconciliation — qui balaie TOUS les avoirs bloqués du tenant — opère sur un état propre.
/// Les pivots de <see cref="CheckIntegrationFixtures"/> portent des numéros uniques (dérivés de la référence
/// source) : les assertions sur le plug-in factice ciblent un numéro précis (job tenant-wide).
/// </remarks>
public sealed class CreditNotePipelineTests : IAsyncLifetime
{
    private readonly PipelineE2ETenant _tenant = new("acme");

    public Task InitializeAsync() => _tenant.InitializeAsync();

    public Task DisposeAsync() => _tenant.DisposeAsync();

    [Fact]
    public async Task Simple_CreditNote_Whose_Origin_Is_Already_Issued_Passes_Check_And_Is_Sent()
    {
        // Facture d'origine émise AVANT que l'avoir ne soit contrôlé : l'avoir passe le CHECK directement.
        var invoice = CheckIntegrationFixtures.BuildPivot("no_ba=cn-simple-origin", "NORMAL");
        var invoiceId = await IngestAndCheckAsync(invoice);
        (await _tenant.GetDocumentStateAsync(invoiceId)).Should().Be("ReadyToSend");
        await _tenant.RunSendAsync();
        (await _tenant.GetDocumentStateAsync(invoiceId)).Should().Be("Issued");

        var avoir = CheckIntegrationFixtures.BuildCreditNote(
            "no_ba=cn-simple-avoir", "NORMAL", new PivotDocumentRefDto(invoice.Number, invoice.IssueDate));
        var avoirId = await IngestAndCheckAsync(avoir);
        (await _tenant.GetDocumentStateAsync(avoirId))
            .Should().Be("ReadyToSend", "la facture d'origine est déjà émise → l'avoir passe le CHECK sans blocage (avoir simple).");

        await _tenant.RunSendAsync();

        (await _tenant.GetDocumentStateAsync(avoirId)).Should().Be("Issued");
        _tenant.PaClient.IssuedDocumentNumbers.Should().Contain(avoir.Number);
    }

    [Fact]
    public async Task CreditNote_Received_Before_Its_Origin_Is_Reordered_And_Sent_After_It()
    {
        var invoice = CheckIntegrationFixtures.BuildPivot("no_ba=cn-reorder-origin", "NORMAL");
        var avoir = CheckIntegrationFixtures.BuildCreditNote(
            "no_ba=cn-reorder-avoir", "NORMAL", new PivotDocumentRefDto(invoice.Number, invoice.IssueDate));

        // 1) L'avoir arrive AVANT sa facture : bloqué au CHECK (facture d'origine inconnue).
        var avoirId = await IngestAndCheckAsync(avoir);
        (await _tenant.GetDocumentStateAsync(avoirId))
            .Should().Be("Blocked", "la facture d'origine est inconnue de la passerelle au CHECK de l'avoir.");

        // 2) La facture d'origine arrive ensuite : prête à l'envoi.
        var invoiceId = await IngestAndCheckAsync(invoice);
        (await _tenant.GetDocumentStateAsync(invoiceId)).Should().Be("ReadyToSend");

        // 3) UN seul SEND : la facture est émise, PUIS l'avoir est débloqué (origine émise) et émis — dans cet ordre.
        await _tenant.RunSendAsync();

        (await _tenant.GetDocumentStateAsync(invoiceId)).Should().Be("Issued", "la facture d'origine est émise d'abord.");
        (await _tenant.GetDocumentStateAsync(avoirId))
            .Should().Be("Issued", "l'avoir est réordonné : débloqué puis émis dans le MÊME traitement, après sa facture (F07-F08 §B.5).");
        _tenant.PaClient.IssuedDocumentNumbers.Should().Contain(avoir.Number);
    }

    [Fact]
    public async Task Orphan_CreditNote_Stays_Blocked_And_Is_Never_Sent()
    {
        // La facture d'origine référencée n'existe pas dans la passerelle et n'arrivera jamais (émise hors passerelle).
        var avoir = CheckIntegrationFixtures.BuildCreditNote(
            "no_ba=cn-orphan", "NORMAL", new PivotDocumentRefDto("F-2026-HORS-PASSERELLE", new DateTime(2026, 1, 10)));
        var avoirId = await IngestAndCheckAsync(avoir);
        (await _tenant.GetDocumentStateAsync(avoirId)).Should().Be("Blocked");

        await _tenant.RunSendAsync();

        (await _tenant.GetDocumentStateAsync(avoirId))
            .Should().Be("Blocked", "un avoir orphelin reste bloqué : aucune référence n'est fabriquée, jamais d'envoi (F07-F08 §B.4).");
        _tenant.PaClient.IssuedDocumentNumbers.Should().NotContain(avoir.Number, "un avoir orphelin n'est jamais émis.");
    }

    [Fact]
    public async Task Grouped_CreditNote_Is_Sent_Only_After_All_Its_Origins_Are_Issued()
    {
        var invoiceA = CheckIntegrationFixtures.BuildPivot("no_ba=cn-grouped-A", "NORMAL");
        var invoiceB = CheckIntegrationFixtures.BuildPivot("no_ba=cn-grouped-B", "NORMAL");
        var avoir = CheckIntegrationFixtures.BuildCreditNote(
            "no_ba=cn-grouped-avoir",
            "NORMAL",
            new PivotDocumentRefDto(invoiceA.Number, invoiceA.IssueDate),
            new PivotDocumentRefDto(invoiceB.Number, invoiceB.IssueDate));

        // 1) Seule la facture A est émise.
        var invoiceAId = await IngestAndCheckAsync(invoiceA);
        await _tenant.RunSendAsync();
        (await _tenant.GetDocumentStateAsync(invoiceAId)).Should().Be("Issued");

        // 2) L'avoir groupé est contrôlé alors que B est encore inconnue → bloqué (une origine manque).
        var avoirId = await IngestAndCheckAsync(avoir);
        (await _tenant.GetDocumentStateAsync(avoirId))
            .Should().Be("Blocked", "un avoir groupé reste bloqué tant que TOUTES ses factures d'origine ne sont pas émises (B manque).");

        // 3) La facture B est émise : le SEND réordonne alors l'avoir (toutes les origines émises) et l'envoie.
        var invoiceBId = await IngestAndCheckAsync(invoiceB);
        await _tenant.RunSendAsync();

        (await _tenant.GetDocumentStateAsync(invoiceBId)).Should().Be("Issued");
        (await _tenant.GetDocumentStateAsync(avoirId))
            .Should().Be("Issued", "l'avoir groupé est débloqué et émis une fois TOUTES ses origines émises (F07-F08 §B.4).");
        _tenant.PaClient.IssuedDocumentNumbers.Should().Contain(avoir.Number);
    }

    [Fact]
    public async Task CreditNote_Blocked_For_Another_Reason_Stays_Blocked_Even_When_Origin_Is_Issued()
    {
        // La facture d'origine EST émise, mais l'avoir porte un régime ABSENT de la table validée : il est bloqué
        // au MAPPING (pas à cause de l'origine). La réconciliation re-évalue le document COMPLET (mapping → garde-fou
        // → validation) — jamais un simple test de l'origine — donc l'avoir RESTE bloqué (« bloquer plutôt qu'envoyer
        // faux », CLAUDE.md n°3). Garantit que la réconciliation ne débloque pas à l'aveugle sur la seule origine.
        var invoice = CheckIntegrationFixtures.BuildPivot("no_ba=cn-otherissue-origin", "NORMAL");
        var invoiceId = await IngestAndCheckAsync(invoice);
        await _tenant.RunSendAsync();
        (await _tenant.GetDocumentStateAsync(invoiceId)).Should().Be("Issued");

        var avoir = CheckIntegrationFixtures.BuildCreditNote(
            "no_ba=cn-otherissue-avoir", "REGIME-INCONNU", new PivotDocumentRefDto(invoice.Number, invoice.IssueDate));
        var avoirId = await IngestAndCheckAsync(avoir);
        (await _tenant.GetDocumentStateAsync(avoirId))
            .Should().Be("Blocked", "régime absent de la table validée → bloqué au mapping (motif distinct de l'origine).");

        await _tenant.RunSendAsync();

        (await _tenant.GetDocumentStateAsync(avoirId))
            .Should().Be("Blocked", "la réconciliation re-évalue le document COMPLET : un avoir bloqué pour un autre motif que l'origine reste bloqué, même origine émise.");
        _tenant.PaClient.IssuedDocumentNumbers.Should().NotContain(avoir.Number);
    }

    /// <summary>Ingestion réelle + CHECK d'un pivot ; retourne l'identifiant du document rangé.</summary>
    private async Task<Guid> IngestAndCheckAsync(PivotDocumentDto pivot)
    {
        var payloadHash = PayloadHasher.ComputeHash(CanonicalJson.Serialize(pivot));
        (await _tenant.IngestAsync(pivot)).Should().Be(DocumentPushStatus.Accepted);

        var documentId = await _tenant.ResolveDocumentIdAsync(pivot.SourceReference, payloadHash);
        documentId.Should().NotBeNull("l'ingestion a rangé le document en Detected.");

        await _tenant.RunCheckAsync(documentId!.Value, pivot.SourceReference, payloadHash);
        return documentId.Value;
    }
}
