namespace Liakont.PaClients.B2Brouter.Tests.Unit;

using System.Net;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Tests PAB02 de l'ENVOI : les 3 familles d'erreurs de F05 §4.1 et l'idempotence anti-doublon de
/// F05 §4.2. Décision de conception (fiscale) : sur une erreur TRANSITOIRE, le client ne re-POSTe PAS
/// à l'aveugle (l'absence d'un numéro n'est pas prouvable tant que la relecture filtrée par numéro
/// n'est pas validée en staging — PAB04) ; il fait UNE relecture d'idempotence (raccroche si la facture
/// existe déjà) puis dégrade en TechnicalError re-tentable au prochain run. Backoff zéro (politique de
/// test). Le retry backoff intra-appel est exercé sur les LECTURES (suite de relecture d'état).
/// </summary>
public sealed class B2BrouterClientErrorHandlingTests
{
    // ── Famille 1 : transitoire (réseau / 5xx / timeout) ──

    [Fact]
    public async Task Transient_5xx_Reads_Idempotence_Once_Then_Degrades_To_Technical_Without_Reposting()
    {
        // POST 503 ; la relecture trouve une liste vide (numéro absent d'une page possiblement incomplète)
        // → on NE re-POSTe PAS (anti-doublon) → TechnicalError re-tentable au prochain run.
        var handler = new RoutedHttpMessageHandler()
            .OnPost(HttpStatusCode.ServiceUnavailable, "oups")
            .OnListInvoices(HttpStatusCode.OK, B2BrouterTestData.EmptyInvoiceListJson);
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError, "5xx = transitoire re-tentable (F05 §4.1)");
        handler.PostCount.Should().Be(1, "jamais de re-POST à l'aveugle : l'absence n'est pas prouvée (PAB04) — anti-doublon (CLAUDE.md n°3)");
        handler.ListCount.Should().Be(1, "une relecture d'idempotence est tentée pour raccrocher une éventuelle facture déjà créée (F05 §4.2)");
    }

    [Fact]
    public async Task Transient_Network_Error_Degrades_To_Technical()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnPostThrows(new HttpRequestException("connexion refusée"))
            .OnListInvoices(HttpStatusCode.OK, B2BrouterTestData.EmptyInvoiceListJson);
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("B2B_NETWORK");
        handler.PostCount.Should().Be(1);
    }

    // ── Famille 2 : 4xx → pas de retry, aucune relecture ──

    [Fact]
    public async Task Rejected_4xx_Is_Not_Retried_And_Never_Triggers_An_Idempotence_Read()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnPost(HttpStatusCode.UnprocessableEntity, """{"errors":[{"code":"INVALID","message":"Payload invalide"}]}""");
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.RejectedByPa);
        handler.PostCount.Should().Be(1, "un 4xx ne se retente pas (F05 §4.1)");
        handler.ListCount.Should().Be(0, "aucune relecture d'idempotence sur un rejet métier (cas terminal, pas transitoire)");
    }

    [Fact]
    public async Task Auth_401_Is_Not_Retried_And_Never_Triggers_An_Idempotence_Read()
    {
        var handler = new RoutedHttpMessageHandler().OnPost(HttpStatusCode.Unauthorized, string.Empty);
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(
            PaSendState.TechnicalError,
            "401 = erreur de config/auth re-tentable au prochain run après correction de la clé (F05 §4.1)");
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("401", "l'auth reste distinguable d'un 5xx par son code HTTP");
        handler.PostCount.Should().Be(1, "retenter une clé invalide ne sert à rien (F05 §4.1 : 401 = config, pas transitoire)");
        handler.ListCount.Should().Be(0, "401 = cas terminal côté client, aucune relecture d'idempotence");
    }

    // ── Famille 3 : 200 + errors[] (erreur silencieuse) → pas de retry, aucune relecture ──

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

    // ── Idempotence (F05 §4.2) : relecture GET après un transitoire, RACCROCHAGE si déjà créé ──

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
        handler.ListCount.Should().Be(1, "relecture GET de la liste du compte après le timeout (F05 §4.2)");
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
    public async Task Reconnect_To_A_Sending_Invoice_Does_Not_Report_Issued()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnPostThrows(new TaskCanceledException("délai dépassé"))
            .OnListInvoices(HttpStatusCode.OK, B2BrouterTestData.InvoiceListJsonWith("F-2026-001", "INV-S", "sending"));
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20("F-2026-001"));

        result.State.Should().Be(PaSendState.Sending, "une facture raccrochée encore « sending » n'est pas confirmée émise");
        result.PaDocumentId.Should().Be("INV-S");
        handler.PostCount.Should().Be(1);
    }

    [Fact]
    public async Task Reconnect_To_An_Invoice_Carrying_Errors_Is_Rejected_Not_Issued()
    {
        const string listWithErrors =
            """[{"id":"INV-E","number":"F-2026-001","state":"issued","errors":[{"code":"VATEX_MISSING","message":"VATEX requis"}]}]""";
        var handler = new RoutedHttpMessageHandler()
            .OnPostThrows(new TaskCanceledException("délai dépassé"))
            .OnListInvoices(HttpStatusCode.OK, listWithErrors);
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20("F-2026-001"));

        result.State.Should().Be(PaSendState.RejectedByPa, "une facture retrouvée AVEC errors[] est un rejet, jamais « émise »");
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("VATEX_MISSING");
    }

    [Fact]
    public async Task Timeout_Then_Idempotence_Read_NotFound_Degrades_To_Technical_Without_Reposting()
    {
        // La relecture renvoie une liste sans le numéro : une PAGE possiblement INCOMPLÈTE ne prouve pas
        // l'absence → on NE re-POSTe PAS (anti-doublon) → re-tentable au prochain run.
        var handler = new RoutedHttpMessageHandler()
            .OnPostThrows(new TaskCanceledException("délai dépassé"))
            .OnListInvoices(HttpStatusCode.OK, B2BrouterTestData.EmptyInvoiceListJson);
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError, "numéro absent d'une page ≠ facture absente → re-tentable, jamais re-POST à l'aveugle");
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("B2B_TIMEOUT");
        handler.PostCount.Should().Be(1, "aucun re-POST tant que l'absence n'est pas prouvée (PAB04) — anti-doublon (CLAUDE.md n°3)");
        handler.ListCount.Should().Be(1);
    }

    [Fact]
    public async Task Timeout_Then_Idempotence_Read_Failing_Degrades_To_Technical()
    {
        // La relecture échoue (503) : on ne peut ni raccrocher ni prouver l'absence → re-tentable.
        var handler = new RoutedHttpMessageHandler()
            .OnPostThrows(new TaskCanceledException("délai dépassé"))
            .OnListInvoices(HttpStatusCode.ServiceUnavailable, "down");
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("B2B_TIMEOUT");
        handler.PostCount.Should().Be(1, "aucun re-POST tant que l'absence n'est pas prouvée (anti-doublon)");
    }

    [Fact]
    public async Task Idempotence_Read_With_Unrecognised_List_Shape_Degrades_To_Technical()
    {
        var handler = new RoutedHttpMessageHandler()
            .OnPostThrows(new TaskCanceledException("délai dépassé"))
            .OnListInvoices(HttpStatusCode.OK, "ceci n'est pas du JSON");
        var client = B2BrouterTestData.CreateClient(handler);

        var result = await client.SendDocumentAsync(B2BrouterTestData.Invoice20());

        result.State.Should().Be(PaSendState.TechnicalError, "forme de liste non reconnue → on ne raccroche pas et on ne re-POSTe pas");
        handler.PostCount.Should().Be(1);
    }
}
