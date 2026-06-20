namespace Liakont.PaClients.ChorusPro.Tests.Unit;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Tests du DÉPÔT <c>deposerFluxFacture</c> (CP04, F18 §3) sur un handler HTTP mocké — aucune PA réelle.
/// Vérifient le contrat clé : dépôt accepté → <see cref="PaSendState.Sending"/> (accusé de réception),
/// JAMAIS <see cref="PaSendState.Issued"/> au dépôt (A1/D5) ; payload camelCase EXACT (artefact base64 +
/// constantes, aucun montant, pas d'<c>idUtilisateurCourant</c>) ; double authentification (Bearer +
/// <c>cpro-account</c>) ; 4xx → Rejected ; 5xx/401 → Technical ; timeout/réseau → Technical SANS re-dépôt
/// (idempotence A3/D8) ; RawResponse conservée sans credential (C5 / CLAUDE.md n°10).
/// </summary>
public sealed class ChorusProSendTests
{
    private const string TechnicalAccount = "bG9naW46bWRw"; // base64(login:mdp) fictif
    private static readonly byte[] Artifact = [0x25, 0x50, 0x44, 0x46]; // « %PDF » — octets opaques de test

    private static PivotDocumentDto Document(string number = "F-2026-CP04") => new(
        sourceDocumentKind: "FA",
        number: number,
        issueDate: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
        sourceReference: "REF-1",
        supplier: null,
        totals: new PivotTotalsDto(100m, 20m, 120m),
        operationCategory: null);

    private static (ChorusProClient Client, RecordingHttpMessageHandler Handler, StubChorusProTokenProvider Tokens) NewSendClient()
    {
        var handler = new RecordingHttpMessageHandler();
        var tokens = new StubChorusProTokenProvider();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://sandbox-api.piste.gouv.fr/cpro/") };
        var client = new ChorusProClient(http, tokens, TechnicalAccount, ChorusProCapabilities.Declared);
        return (client, handler, tokens);
    }

    private static PaSendContext ContextWithArtifact() => new(Artifact);

    [Fact]
    public async Task Deposit_Accepted_Maps_To_Sending_With_Flux_Number_Never_Issued()
    {
        var (client, handler, _) = NewSendClient();
        handler.Respond(HttpStatusCode.OK, """{"codeRetour":"0","libelle":"Dépôt accepté","numeroFluxDepot":"FLUX-123"}""");

        var result = await client.SendDocumentAsync(Document(), context: ContextWithArtifact());

        result.State.Should().Be(PaSendState.Sending, "le dépôt accusé réception est Sending, jamais Issued (A1/D5)");
        result.State.Should().NotBe(PaSendState.Issued);
        result.PaDocumentId.Should().Be("FLUX-123");
        handler.CallCount.Should().Be(1, "un seul dépôt POST");
        handler.Requests[0].Uri!.AbsolutePath.Should().EndWith("/cpro/factures/v1/deposer");
    }

    [Fact]
    public async Task Deposit_Payload_Carries_Base64_Artifact_And_Constants_Without_IdUtilisateur()
    {
        var (client, handler, _) = NewSendClient();
        handler.Respond(HttpStatusCode.OK, """{"numeroFluxDepot":"FLUX-1"}""");

        await client.SendDocumentAsync(Document("F-2026-042"), context: ContextWithArtifact());

        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        var root = body.RootElement;
        root.GetProperty("fichierFlux").GetString().Should().Be(Convert.ToBase64String(Artifact), "fichierFlux = base64 de l'artefact scellé (transport pur)");
        root.GetProperty("syntaxeFlux").GetString().Should().Be("IN_DP_E2_CII_FACTURX");
        root.GetProperty("avecSignature").GetBoolean().Should().BeFalse("notre artefact est non signé (D9)");
        root.GetProperty("nomFichier").GetString().Should().Contain("F-2026-042");
        root.TryGetProperty("idUtilisateurCourant", out _).Should().BeFalse("cardinalité non verrouillée → champ omis (F18 §3.2, CLAUDE.md n°2)");
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["fichierFlux", "nomFichier", "syntaxeFlux", "avecSignature"],
            "transport PUR : exactement ces 4 champs (aucun montant, aucun idUtilisateurCourant non verrouillé)");
    }

    [Fact]
    public async Task Deposit_Applies_Double_Authentication_Bearer_And_CproAccount()
    {
        var (client, handler, _) = NewSendClient();
        handler.Respond(HttpStatusCode.OK, """{"numeroFluxDepot":"FLUX-1"}""");

        await client.SendDocumentAsync(Document(), context: ContextWithArtifact());

        var request = handler.Requests[0];
        request.Authorization!.Scheme.Should().Be("Bearer");
        request.Authorization.Parameter.Should().Be(StubChorusProTokenProvider.NominalToken);
        request.TechnicalAccount.Should().Be(TechnicalAccount, "l'en-tête cpro-account du compte technique accompagne CHAQUE requête (F18 §2.2)");
    }

    [Fact]
    public async Task Deposit_2xx_Without_Flux_Number_Is_Rejected_Never_Sending()
    {
        var (client, handler, _) = NewSendClient();
        handler.Respond(HttpStatusCode.OK, """{"codeRetour":"9","libelle":"Flux non conforme"}""");

        var result = await client.SendDocumentAsync(Document(), context: ContextWithArtifact());

        result.State.Should().Be(PaSendState.RejectedByPa, "2xx sans accusé de réception = rejet métier silencieux");
        result.State.Should().NotBe(PaSendState.Sending);
        result.Errors.Should().ContainSingle().Which.Message.Should().Be("Flux non conforme");
        result.RawResponse.Should().Contain("Flux non conforme");
    }

    [Fact]
    public async Task Business_4xx_Is_Rejected_With_Pa_Errors_Intact()
    {
        var (client, handler, _) = NewSendClient();
        handler.Respond(HttpStatusCode.BadRequest, """{"codeRetour":"412","libelle":"Syntaxe de flux invalide"}""");

        var result = await client.SendDocumentAsync(Document(), context: ContextWithArtifact());

        result.State.Should().Be(PaSendState.RejectedByPa);
        result.Errors.Should().ContainSingle();
        result.Errors[0].Code.Should().Be("412");
        result.Errors[0].Message.Should().Be("Syntaxe de flux invalide");
        result.RawResponse.Should().Contain("Syntaxe de flux invalide");
    }

    [Fact]
    public async Task Server_5xx_Is_Technical_Retryable()
    {
        var (client, handler, _) = NewSendClient();
        handler.Respond(HttpStatusCode.InternalServerError, """{"message":"Indisponible"}""");

        var result = await client.SendDocumentAsync(Document(), context: ContextWithArtifact());

        result.State.Should().Be(PaSendState.TechnicalError);
        result.State.Should().NotBe(PaSendState.RejectedByPa, "un 5xx est re-tentable, pas un rejet métier figé");
    }

    [Fact]
    public async Task Auth_401_Triggers_One_Refresh_Then_Maps_To_Technical()
    {
        var (client, handler, tokens) = NewSendClient();
        handler.Respond(HttpStatusCode.Unauthorized, "{}");
        handler.Respond(HttpStatusCode.Unauthorized, "{}");

        var result = await client.SendDocumentAsync(Document(), context: ContextWithArtifact());

        result.State.Should().Be(PaSendState.TechnicalError, "401 = problème d'auth/config, re-tentable (F18 §5)");
        tokens.ForceRefreshCount.Should().Be(1, "un 401 déclenche un refresh forcé puis UNE seconde tentative (F18 §2.1)");
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Timeout_Maps_To_Technical_And_Never_Reposts()
    {
        var (client, handler, _) = NewSendClient();
        handler.Throws(new TaskCanceledException("timeout"));

        var result = await client.SendDocumentAsync(Document(), context: ContextWithArtifact());

        result.State.Should().Be(PaSendState.TechnicalError);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("CPRO_TIMEOUT");
        handler.CallCount.Should().Be(1, "un timeout ne re-dépose JAMAIS (idempotence A3/D8 — sinon double facture)");
    }

    [Fact]
    public async Task Network_Error_Maps_To_Technical_Without_Repost()
    {
        var (client, handler, _) = NewSendClient();
        handler.Throws(new HttpRequestException("connexion refusée"));

        var result = await client.SendDocumentAsync(Document(), context: ContextWithArtifact());

        result.State.Should().Be(PaSendState.TechnicalError);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("CPRO_NETWORK");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RawResponse_Never_Leaks_Credentials()
    {
        var (client, handler, _) = NewSendClient();
        handler.Respond(HttpStatusCode.OK, """{"numeroFluxDepot":"FLUX-1"}""");

        var result = await client.SendDocumentAsync(Document(), context: ContextWithArtifact());

        result.RawResponse.Should().NotContain(TechnicalAccount, "le RawResponse (corps réponse) ne porte jamais l'en-tête cpro-account");
        result.RawResponse.Should().NotContain(StubChorusProTokenProvider.NominalToken, "ni le jeton bearer (CLAUDE.md n°10)");
    }

    [Fact]
    public async Task Missing_Artifact_Blocks_Without_Any_Http_Call()
    {
        var (client, handler, _) = NewSendClient();

        var result = await client.SendDocumentAsync(Document(), context: new PaSendContext(ReadOnlyMemory<byte>.Empty));

        result.State.Should().Be(PaSendState.TechnicalError);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("CPRO_ARTEFACT_REQUIS");
        handler.CallCount.Should().Be(0, "la garde artefact bloque AVANT tout HTTP (jamais régénéré, jamais émis à vide)");
    }

    [Fact]
    public async Task Auth_403_Is_Technical_Retryable_Not_Rejected()
    {
        // 403 = problème d'auth/habilitation (F18 §5, IsRetryableStatus couvre 403) : re-tentable,
        // jamais un rejet métier figé (ce n'est pas Chorus Pro qui refuse le document).
        var (client, handler, _) = NewSendClient();
        handler.Respond(HttpStatusCode.Forbidden, """{"message":"Accès refusé"}""");

        var result = await client.SendDocumentAsync(Document(), context: ContextWithArtifact());

        result.State.Should().Be(PaSendState.TechnicalError, "403 est re-tentable (F18 §5), jamais un rejet métier");
        result.State.Should().NotBe(PaSendState.RejectedByPa);
    }

    [Fact]
    public async Task Unexpected_3xx_Is_Technical_Not_Rejected()
    {
        // 3xx non-suivie sur un POST (ex : 307 TemporaryRedirect) → bras 1xx/3xx de MapDeposit :
        // problème de routage/réseau, re-tentable SANS re-dépôt (A3/D8). Jamais masqué en rejet métier.
        var (client, handler, _) = NewSendClient();
        handler.Respond(HttpStatusCode.TemporaryRedirect, "{}");

        var result = await client.SendDocumentAsync(Document(), context: ContextWithArtifact());

        result.State.Should().Be(PaSendState.TechnicalError, "307 inattendu = routage/réseau, re-tentable (A3/D8)");
        result.State.Should().NotBe(PaSendState.RejectedByPa, "un 3xx n'est pas un refus métier de Chorus Pro");
    }

    [Fact]
    public async Task Deposit_2xx_With_Numeric_Flux_Number_Maps_To_Sending()
    {
        // Couvre JsonValueKind.Number dans TryReadFluxNumber : certaines réponses Chorus Pro renvoient
        // numeroFluxDepot en tant que nombre JSON (ex : 123) plutôt que chaîne — doit être accepté.
        var (client, handler, _) = NewSendClient();
        handler.Respond(HttpStatusCode.OK, """{"numeroFluxDepot":123}""");

        var result = await client.SendDocumentAsync(Document(), context: ContextWithArtifact());

        result.State.Should().Be(PaSendState.Sending, "numeroFluxDepot numérique → accusé de réception valide");
        result.PaDocumentId.Should().Be("123", "le numéro de flux numérique est converti en string via GetRawText()");
    }
}
