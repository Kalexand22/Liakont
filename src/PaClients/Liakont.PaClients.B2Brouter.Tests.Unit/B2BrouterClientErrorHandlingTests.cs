namespace Liakont.PaClients.B2Brouter.Tests.Unit;

using System.Net;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Tests PAB02 : gestion des 3 familles d'erreurs de F05 §4.1 (retry/backoff sur le transitoire, pas de
/// retry sur les rejets métier ni l'auth) et idempotence anti-doublon de F05 §4.2 (relecture de la liste
/// du compte avant tout re-POST). Backoff zéro (politique de test) pour s'exécuter sans attente réelle.
/// </summary>
public sealed class B2BrouterClientErrorHandlingTests
{
    // ── Famille 1 : transitoire (réseau / 5xx / timeout) → retry avec backoff ──

    [Fact]
    public async Task Transient_5xx_Is_Retried_Up_To_The_Policy_Then_Degrades_To_Technical()
    {
        // POST toujours 503 ; relecture d'idempotence toujours « liste vide » (numéro absent) → re-POST.
        var handler = new RoutedHttpMessageHandler()
            .OnPost(HttpStatusCode.ServiceUnavailable, "oups")
            .OnListInvoices(HttpStatusCode.OK, B2BrouterTestData.EmptyInvoiceListJson);
        var client = B2BrouterTestData.CreateClient(handler); // NoDelay(3)

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError, "au-delà des réessais, F05 §4.1 dégrade en technique re-tentable");
        handler.PostCount.Should().Be(4, "tentative initiale + 3 réessais (backoff 5 s / 30 s / 2 min, F05 §4.1)");
        handler.ListCount.Should().Be(3, "une relecture d'idempotence avant chacun des 3 re-POST (F05 §4.2)");
    }

    [Fact]
    public async Task Retry_Count_Follows_The_Injected_Policy()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnPost(HttpStatusCode.ServiceUnavailable, "oups")
            .OnListInvoices(HttpStatusCode.OK, B2BrouterTestData.EmptyInvoiceListJson);
        var client = B2BrouterTestData.CreateClient(handler, retryPolicy: B2BrouterRetryPolicy.NoDelay(1));

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError);
        handler.PostCount.Should().Be(2, "1 réessai = tentative initiale + 1");
    }

    // ── Famille 2 : 4xx → pas de retry ──

    [Fact]
    public async Task Rejected_4xx_Is_Not_Retried_And_Never_Triggers_An_Idempotence_Read()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnPost(HttpStatusCode.UnprocessableEntity, """{"errors":[{"code":"INVALID","message":"Payload invalide"}]}""");
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.RejectedByPa);
        handler.PostCount.Should().Be(1, "un 4xx ne se retente pas (F05 §4.1)");
        handler.ListCount.Should().Be(0, "aucune relecture d'idempotence sur un rejet métier (rien n'a pu être créé)");
    }

    [Fact]
    public async Task Auth_401_Is_Not_Retried()
    {
        var handler = new RoutedHttpMessageHandler().OnPost(HttpStatusCode.Unauthorized, string.Empty);
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(
            PaSendState.TechnicalError,
            "401 = erreur de config/auth re-tentable au prochain run après correction de la clé (F05 §4.1)");
        handler.PostCount.Should().Be(1, "retenter une clé invalide 3 fois ne sert à rien (F05 §4.1 : 401 = config, pas transitoire)");
        handler.ListCount.Should().Be(0);
    }

    // ── Famille 3 : 200 + errors[] (erreur silencieuse) → pas de retry ──

    [Fact]
    public async Task Silent_Error_200_With_Errors_Is_Not_Retried()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnPost(HttpStatusCode.OK, """{"id":"INV-3","state":"issued","errors":[{"code":"VATEX_MISSING","message":"VATEX requis"}]}""");
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.RejectedByPa, "une erreur silencieuse (200 + errors[]) est un rejet, pas un transitoire (F05 §4.1)");
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("VATEX_MISSING");
        handler.PostCount.Should().Be(1);
        handler.ListCount.Should().Be(0);
    }

    // ── Idempotence (F05 §4.2) : relecture GET avant re-POST ──

    [Fact]
    public async Task Timeout_Then_Idempotence_Read_Finds_The_Invoice_So_It_Reconnects_Without_Reposting()
    {
        // Le POST « timeoute » (envoyé ou pas ?). La relecture trouve la facture DÉJÀ créée → on raccroche.
        var handler = new RoutedHttpMessageHandler()
            .OnPostThrows(new TaskCanceledException("délai dépassé"))
            .OnListInvoices(HttpStatusCode.OK, B2BrouterTestData.InvoiceListJsonWith("F-2026-001", "INV-RC", "issued"));
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20("F-2026-001"));

        result.State.Should().Be(PaSendState.Issued, "la facture existait déjà côté PA — on raccroche son état (F05 §4.2)");
        result.PaDocumentId.Should().Be("INV-RC", "on raccroche l'identifiant de la facture retrouvée");
        handler.PostCount.Should().Be(1, "on ne re-POSTe JAMAIS un numéro déjà créé (anti-doublon fiscal — F05 §4.2)");
        handler.ListCount.Should().Be(1, "relecture GET de la liste du compte avant tout re-POST (F05 §4.2)");
    }

    [Fact]
    public async Task Timeout_Then_Idempotence_Read_Tolerates_The_Wrapped_List_Shape()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnPostThrows(new TaskCanceledException("délai dépassé"))
            .OnListInvoices(HttpStatusCode.OK, B2BrouterTestData.WrappedInvoiceListJsonWith("F-2026-001", "INV-W", "issued"));
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20("F-2026-001"));

        result.State.Should().Be(PaSendState.Issued);
        result.PaDocumentId.Should().Be("INV-W", "la forme enveloppée invoices est tolérée (F05 §4.2)");
    }

    [Fact]
    public async Task Timeout_Then_Idempotence_Read_Confirms_Absent_So_It_Reposts()
    {
        // La relecture confirme l'ABSENCE du numéro → re-POST sûr, qui réussit.
        var handler = new RoutedHttpMessageHandler()
            .OnPostThrows(new TaskCanceledException("délai dépassé"))
            .OnPost(HttpStatusCode.OK, B2BrouterTestData.IssuedJson)
            .OnListInvoices(HttpStatusCode.OK, B2BrouterTestData.EmptyInvoiceListJson);
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.Issued);
        result.PaDocumentId.Should().Be("INV-1001");
        handler.PostCount.Should().Be(2, "absence confirmée → un re-POST sûr (F05 §4.2)");
        handler.ListCount.Should().Be(1);
    }

    [Fact]
    public async Task Timeout_Then_Idempotence_Read_Inconclusive_Bails_Without_Reposting()
    {
        // La relecture échoue (503) : impossible de garantir l'absence d'un doublon → on n'ose PAS re-POSTer.
        var handler = new RoutedHttpMessageHandler()
            .OnPostThrows(new TaskCanceledException("délai dépassé"))
            .OnListInvoices(HttpStatusCode.ServiceUnavailable, "down");
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError, "relecture non concluante → re-tentable au prochain run, jamais un re-POST à l'aveugle");
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("B2B_TIMEOUT");
        handler.PostCount.Should().Be(1, "aucun re-POST tant que l'absence n'est pas CONFIRMÉE (anti-doublon — CLAUDE.md n°3)");
        handler.ListCount.Should().Be(1);
    }

    [Fact]
    public async Task Idempotence_Read_With_Unrecognised_List_Shape_Is_Inconclusive_And_Bails()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnPostThrows(new TaskCanceledException("délai dépassé"))
            .OnListInvoices(HttpStatusCode.OK, "ceci n'est pas du JSON");
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError, "forme de liste non reconnue = non concluant, jamais « absent » (pas de re-POST)");
        handler.PostCount.Should().Be(1);
    }

    [Fact]
    public async Task Transient_5xx_Then_Absent_Reposts_And_Succeeds()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnPost(HttpStatusCode.ServiceUnavailable, "oups")
            .OnPost(HttpStatusCode.OK, B2BrouterTestData.IssuedJson)
            .OnListInvoices(HttpStatusCode.OK, B2BrouterTestData.EmptyInvoiceListJson);
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.Issued);
        handler.PostCount.Should().Be(2);
        handler.ListCount.Should().Be(1);
    }
}
