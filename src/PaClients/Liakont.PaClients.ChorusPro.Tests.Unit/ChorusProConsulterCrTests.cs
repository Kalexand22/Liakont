namespace Liakont.PaClients.ChorusPro.Tests.Unit;

using System.Net;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Couvre la relecture d'état <c>consulterCR</c> de CP05 (F18 §4) portée par
/// <see cref="ChorusProClient.GetDocumentStatusAsync"/> : mapping EXACT des 9 libellés
/// <c>etatCourantFlux</c> → <see cref="PaSendState"/> (<c>Intégré</c> → <see cref="PaSendState.Issued"/>
/// SEUL, A1/D5), fail-safe « jamais <c>Issued</c> » sur valeur inconnue (CLAUDE.md n°3), lecture idempotente
/// (retry/backoff sur le transitoire), conservation des <see cref="PaError"/> + <c>RawResponse</c>, et double
/// authentification posée sur la requête. Aucune PA réelle : l'HTTP est mocké, le jeton est un stub.
/// </summary>
public sealed class ChorusProConsulterCrTests
{
    private const string TechnicalAccountHeader = "bG9naW46bWRw"; // base64("login:mdp") — fictif.
    private const string FluxId = "FLUX-DEPOT-1";

    [Theory]
    [InlineData("Reçu", PaSendState.Sending)]
    [InlineData("Traité SE CPP", PaSendState.Sending)]
    [InlineData("En attente de traitement", PaSendState.Sending)]
    [InlineData("En cours de traitement", PaSendState.Sending)]
    [InlineData("En attente de retraitement", PaSendState.Sending)]
    [InlineData("Incidenté", PaSendState.RejectedByPa)]
    [InlineData("Rejeté", PaSendState.RejectedByPa)]
    [InlineData("Intégré", PaSendState.Issued)]
    [InlineData("Intégré partiellement", PaSendState.RejectedByPa)]
    public async Task The_Nine_EtatCourantFlux_Labels_Map_Exactly(string etat, PaSendState expected)
    {
        var body = CrBody(etat);
        var handler = new RecordingHttpMessageHandler().Respond(HttpStatusCode.OK, body);
        var status = await NewClient(handler).GetDocumentStatusAsync(FluxId);

        status.State.Should().Be(expected, $"« {etat} » est mappé par F18 §4");
        status.PaDocumentId.Should().Be(FluxId);
        status.RawResponse.Should().Be(body, "le compte rendu brut est conservé pour l'audit (F06/DR6)");
    }

    [Fact]
    public async Task Integre_Is_The_Only_Path_To_Issued()
    {
        var handler = new RecordingHttpMessageHandler().Respond(HttpStatusCode.OK, CrBody("Intégré"));
        var status = await NewClient(handler).GetDocumentStatusAsync(FluxId);

        status.State.Should().Be(PaSendState.Issued);
        status.Errors.Should().BeEmpty("un flux intégré ne porte pas d'erreur");
    }

    [Theory]
    [InlineData("Rejeté")]
    [InlineData("Incidenté")]
    [InlineData("Intégré partiellement")]
    public async Task Rejection_States_Carry_A_French_PaError_And_Keep_The_Raw_Response(string etat)
    {
        var body = CrBody(etat);
        var handler = new RecordingHttpMessageHandler().Respond(HttpStatusCode.OK, body);
        var status = await NewClient(handler).GetDocumentStatusAsync(FluxId);

        status.State.Should().Be(PaSendState.RejectedByPa);
        status.Errors.Should().ContainSingle().Which.Code.Should().Be(etat);
        status.RawResponse.Should().Be(body);
    }

    [Fact]
    public async Task An_Unknown_Label_Is_Fail_Safe_Sending_Never_Issued()
    {
        var handler = new RecordingHttpMessageHandler().Respond(HttpStatusCode.OK, CrBody("Statut bidon non documenté"));
        var status = await NewClient(handler).GetDocumentStatusAsync(FluxId);

        status.State.Should().Be(PaSendState.Sending);
        status.State.Should().NotBe(PaSendState.Issued, "une valeur inconnue ne devient JAMAIS Issued (CLAUDE.md n°3)");
    }

    [Fact]
    public async Task A_2xx_Without_EtatCourantFlux_Is_Fail_Safe_Sending_Never_Issued()
    {
        var handler = new RecordingHttpMessageHandler().Respond(HttpStatusCode.OK, "{}");
        var status = await NewClient(handler).GetDocumentStatusAsync(FluxId);

        status.State.Should().Be(PaSendState.Sending);
        status.State.Should().NotBe(PaSendState.Issued);
    }

    [Fact]
    public async Task A_4xx_Is_RejectedByPa_With_Errors_And_Raw_Response()
    {
        var body = "{\"message\":\"flux invalide\"}";
        var handler = new RecordingHttpMessageHandler().Respond(HttpStatusCode.BadRequest, body);
        var status = await NewClient(handler).GetDocumentStatusAsync(FluxId);

        status.State.Should().Be(PaSendState.RejectedByPa);
        status.Errors.Should().ContainSingle().Which.Code.Should().Be("400");
        status.RawResponse.Should().Be(body);
    }

    [Fact]
    public async Task A_5xx_Is_TechnicalError_And_Retried_On_The_Transient()
    {
        // La file épuisée rejoue la dernière réponse → 500 à chaque tentative.
        var handler = new RecordingHttpMessageHandler().Respond(HttpStatusCode.InternalServerError, "boom");
        var policy = ChorusProRetryPolicy.NoDelay(3);
        var status = await NewClient(handler, policy).GetDocumentStatusAsync(FluxId);

        status.State.Should().Be(PaSendState.TechnicalError);
        status.RawResponse.Should().Be("boom", "la réponse brute est conservée même en erreur technique");
        handler.CallCount.Should().Be(policy.RetryCount + 1, "le transitoire est ré-essayé jusqu'à épuisement (1 + RetryCount)");
    }

    [Fact]
    public async Task A_Timeout_Is_TechnicalError_And_Retried()
    {
        var handler = new RecordingHttpMessageHandler().Throws(new TaskCanceledException("timeout"));
        var policy = ChorusProRetryPolicy.NoDelay(2);
        var status = await NewClient(handler, policy).GetDocumentStatusAsync(FluxId);

        status.State.Should().Be(PaSendState.TechnicalError);
        status.Errors.Should().ContainSingle().Which.Code.Should().Be("CPRO_TIMEOUT");
        handler.CallCount.Should().Be(policy.RetryCount + 1);
    }

    [Fact]
    public async Task A_Network_Error_Is_TechnicalError()
    {
        var handler = new RecordingHttpMessageHandler().Throws(new HttpRequestException("réseau coupé"));
        var status = await NewClient(handler, ChorusProRetryPolicy.NoDelay(0)).GetDocumentStatusAsync(FluxId);

        status.State.Should().Be(PaSendState.TechnicalError);
        status.Errors.Should().ContainSingle().Which.Code.Should().Be("CPRO_NETWORK");
    }

    [Fact]
    public async Task The_Read_Posts_The_FluxId_With_Both_Auth_Headers()
    {
        var handler = new RecordingHttpMessageHandler().Respond(HttpStatusCode.OK, CrBody("Reçu"));
        await NewClient(handler).GetDocumentStatusAsync(FluxId);

        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Uri!.AbsoluteUri.Should().Contain("consulterCR");
        sent.Body.Should().Contain("numeroFluxDepot").And.Contain(FluxId);
        sent.Authorization!.Scheme.Should().Be("Bearer");
        sent.TechnicalAccount.Should().Be(TechnicalAccountHeader, "le cpro-account est posé à la relecture (F18 §2.2)");
    }

    private static string CrBody(string etat) => $"{{\"etatCourantFlux\":\"{etat}\"}}";

    // Le client réel résout les chemins métier RELATIFS sur la base API du compte (HttpClient.BaseAddress,
    // fournie par le resolver — F18 §3.3). Le test reproduit cette base pour que consulterCR se résolve.
    private static ChorusProClient NewClient(HttpMessageHandler handler, ChorusProRetryPolicy? retryPolicy = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://sandbox-api.piste.gouv.fr/cpro/") },
            new StubChorusProTokenProvider(),
            TechnicalAccountHeader,
            ChorusProCapabilities.Declared,
            retryPolicy ?? ChorusProRetryPolicy.NoDelay(0));
}
