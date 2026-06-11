namespace Liakont.PaClients.SuperPdp;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Plug-in PA Super PDP — implémentation d'<see cref="IPaClient"/> (F14). Encapsule TOUTES les
/// interactions Super PDP (URLs, en-têtes, OAuth, format JSON) : aucun autre composant ne connaît ces
/// détails (F14 §1), c'est ce qui rend le produit indépendant de toute PA (blueprint.md §2 ; CLAUDE.md
/// n°6). Le type est <c>internal</c> : il ne fuit pas hors de l'assembly (acceptance PAS02) — la fabrique
/// le rend derrière l'abstraction <see cref="IPaClient"/>.
/// <para>
/// PÉRIMÈTRE PAS02 = l'ÉMISSION B2C (<see cref="SendDocumentAsync"/> : auth OAuth bearer, transformation
/// pivot → JSON, POST) AVEC la gestion des 3 familles d'erreurs (F14 §4.1 : transitoire / rejet métier
/// 4xx / erreur silencieuse 200 + <c>errors[]</c>) et la relecture d'idempotence anti-doublon ; plus la
/// relecture d'état (<see cref="GetDocumentStatusAsync"/> : polling, F14 §3.4). Les capacités déclarées
/// (<see cref="SuperPdpCapabilities"/>) reflètent CE périmètre : SEUL le B2C est vérifié (F14 §5) ; toute
/// capacité non déclarée dégrade en résultat TYPÉ (jamais d'exception, jamais de blocage produit —
/// invariant PAA01). Les tax reports / réglage / facture générée sont confirmés en sandbox et livrés par
/// PAS03 ; jusque-là, leurs lectures lèvent une <see cref="NotImplementedException"/> traçable plutôt que
/// de renvoyer une donnée fiscale fausse depuis un endpoint non confirmé (CLAUDE.md n°3).
/// </para>
/// </summary>
internal sealed class SuperPdpClient : IPaClient
{
    private readonly HttpClient _httpClient;
    private readonly ISuperPdpTokenProvider _tokenProvider;
    private readonly SuperPdpClientOptions _options;

    /// <summary>
    /// Construit le client pour UN compte. Le <paramref name="httpClient"/> est déjà configuré par la
    /// fabrique (URL de base, TLS, délai) ; le <paramref name="tokenProvider"/> gère le jeton OAuth bearer
    /// — le client ne manipule jamais les secrets en clair (CLAUDE.md n°10).
    /// </summary>
    /// <param name="httpClient">Client HTTP configuré pour le compte (URL de base + TLS).</param>
    /// <param name="tokenProvider">Fournisseur du jeton OAuth (cache + refresh, interne au plug-in).</param>
    /// <param name="options">Configuration non sensible du client (compte + capacités + retry).</param>
    public SuperPdpClient(HttpClient httpClient, ISuperPdpTokenProvider tokenProvider, SuperPdpClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _options = options;
    }

    /// <inheritdoc />
    public PaCapabilities Capabilities => _options.Capabilities;

    /// <inheritdoc />
    public async Task<PaSendResult> SendDocumentAsync(
        PivotDocumentDto document,
        bool sendAfterImport = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        // Avoir demandé alors que la capacité n'est pas déclarée → résultat typé, jamais d'exception ni de
        // blocage produit (invariant PAA01). Le modèle d'avoir Super PDP est confirmé en sandbox (PAS03,
        // O7) avant d'activer SupportsCreditNotes — V1 ne déclare PAS la capacité (F14 §5).
        if (document.CreditNoteRefs.Count > 0 && !Capabilities.SupportsCreditNotes)
        {
            return PaSendResult.NotSupported(
                PaCapabilityNotSupportedResult.Create(Capabilities.PaName, PaCapability.CreditNotes));
        }

        // Construction + sérialisation du payload AVANT toute tentative HTTP : un document mal formé
        // (ligne multi-ventilation) lève ici, AVANT le premier appel PA — jamais tronqué en silence
        // (CLAUDE.md n°3), jamais envoyé partiellement.
        var payload = SuperPdpPayloadBuilder.Build(document, sendAfterImport);
        var json = JsonSerializer.Serialize(payload, SuperPdpJson.Options);
        var url = SendUrl();

        var outcome = await TryPostAsync(url, json, cancellationToken).ConfigureAwait(false);
        if (!outcome.IsTransient)
        {
            // Terminal : émis (issued/new/sending), rejet métier (4xx, 200 + errors[]) ou auth (401/403,
            // déjà retentée une fois avec jeton rafraîchi). Aucun de ces cas ne se re-tente (F14 §4.1).
            return outcome.Result;
        }

        // Erreur TRANSITOIRE (réseau / 5xx / timeout) : on NE ré-émet PAS à l'aveugle. On fait UNE relecture
        // d'idempotence : si la facture existe déjà (numéro), on raccroche son état ; sinon on dégrade en
        // TechnicalError, RE-TENTABLE AU PROCHAIN RUN. La forme exacte de la liste est confirmée en sandbox
        // (PAS03) : une liste illisible/incomplète reste NON CONCLUANTE (jamais « facture absente » → jamais
        // de doublon — CLAUDE.md n°3).
        var reconnect = await TryReconnectByNumberAsync(document.Number, cancellationToken).ConfigureAwait(false);
        return reconnect.Found ? reconnect.Result! : outcome.Result;
    }

    /// <inheritdoc />
    public Task<PaSendResult> SendPaymentReportAsync(
        PaymentReportPeriod period,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        cancellationToken.ThrowIfCancellationRequested();

        // Super PDP ne déclare AUCUN flux de paiement en V1 (flux 10.4/10.2 non documentés — F14 §5, O3) :
        // l'appel dégrade en résultat typé piloté par la capacité (jamais d'exception — PAA01).
        if (!Capabilities.SupportsPaymentReport(period.Flux))
        {
            var capability = period.Flux == PaymentReportFlux.Domestic
                ? PaCapability.DomesticPaymentReporting
                : PaCapability.InternationalPaymentReporting;
            return Task.FromResult(PaSendResult.NotSupported(
                PaCapabilityNotSupportedResult.Create(Capabilities.PaName, capability)));
        }

        // Inatteignable tant que la capacité est false (Super PDP ne déclare aucun flux de paiement — F14
        // §5). Si une version future les active, SEULE la déclaration de capacité changera, et cette branche
        // sera implémentée — aucun autre code produit n'est impacté (CLAUDE.md n°8).
        throw new NotSupportedException(
            "Reporting de paiement Super PDP (flux 10.4/10.2) : capacité non déclarée (F14 §5, O3). Cette " +
            "branche ne s'active que lorsque le support Super PDP aura confirmé ces flux (PAS03).");
    }

    /// <inheritdoc />
    public async Task<PaDocumentStatus> GetDocumentStatusAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paDocumentId);
        cancellationToken.ThrowIfCancellationRequested();

        // Relecture d'état (polling, F14 §3.4) : GET /v1.beta/invoices/{id}. Une lecture est NATURELLEMENT
        // idempotente → simple retry sur le transitoire (réseau / 5xx / timeout), sans garde anti-doublon.
        // Les rejets (4xx) et l'auth (401/403) ne sont pas ré-essayés (F14 §4.1).
        var url = StatusUrl(paDocumentId);
        var policy = _options.RetryPolicy;
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await TryReadStatusAsync(url, paDocumentId, cancellationToken).ConfigureAwait(false);

            if (!outcome.IsTransient || attempt >= policy.RetryCount)
            {
                return outcome.Status;
            }

            await Task.Delay(policy.Backoffs[attempt], cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PaTaxReport>> ListTaxReportsAsync(
        DateTime? since = null,
        CancellationToken cancellationToken = default) =>
        throw NotYetConfirmedInSandbox(nameof(ListTaxReportsAsync));

    /// <inheritdoc />
    public Task<PaTaxReport> GetTaxReportAsync(
        string taxReportId,
        CancellationToken cancellationToken = default) =>
        throw NotYetConfirmedInSandbox(nameof(GetTaxReportAsync));

    /// <inheritdoc />
    public Task<PaAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default) =>
        throw NotYetConfirmedInSandbox(nameof(GetAccountInfoAsync));

    /// <inheritdoc />
    public Task<PaTaxReportSetting> GetTaxReportSettingAsync(CancellationToken cancellationToken = default) =>
        throw NotYetConfirmedInSandbox(nameof(GetTaxReportSettingAsync));

    /// <inheritdoc />
    public Task EnsureTaxReportSettingAsync(
        PaTaxReportSettingRequest request,
        CancellationToken cancellationToken = default) =>
        throw NotYetConfirmedInSandbox(nameof(EnsureTaxReportSettingAsync));

    /// <inheritdoc />
    public Task<PaGeneratedDocument> GetGeneratedDocumentAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paDocumentId);
        cancellationToken.ThrowIfCancellationRequested();

        // L'endpoint de téléchargement de la facture GÉNÉRÉE par Super PDP n'est PAS confirmé (F14 §3.6,
        // O4). Faute de vérification, la capacité reste false → résultat TYPÉ NotSupported, jamais
        // d'exception ni de blocage produit (invariant PAA01). Quand l'endpoint sera validé (sandbox
        // PAS03), SEULES la déclaration de capacité et la branche ci-dessous changeront.
        if (!Capabilities.SupportsDocumentRetrieval)
        {
            return Task.FromResult(PaGeneratedDocument.NotSupported(
                PaCapabilityNotSupportedResult.Create(Capabilities.PaName, PaCapability.DocumentRetrieval)));
        }

        // Inatteignable tant que la capacité est false : garde fail-closed contre une activation de la
        // capacité sans endpoint confirmé (CLAUDE.md n°2/3).
        throw new NotSupportedException(
            "Téléchargement de la facture générée Super PDP : endpoint non confirmé (F14 §3.6, O4). La " +
            "capacité DocumentRetrieval doit rester false tant que le contrat n'est pas validé en sandbox (PAS03).");
    }

    // ── Helpers privés STATIQUES (avant les helpers d'instance — ordre StyleCop) ──

    // URL d'émission / de liste : /v1.beta/invoices (relatif à la base du compte, F14 §3.2). Pas
    // d'identifiant de compte dans l'URL : le compte est porté par le jeton OAuth (client credentials).
    private static string SendUrl() => $"{SuperPdpDefaults.ApiVersionPrefix}/{SuperPdpDefaults.InvoicesPath}";

    // URL de relecture d'état : /v1.beta/invoices/{id} (F14 §3.4).
    private static string StatusUrl(string paDocumentId) =>
        $"{SuperPdpDefaults.ApiVersionPrefix}/{SuperPdpDefaults.InvoicesPath}/{Uri.EscapeDataString(paDocumentId)}";

    private static PaDocumentStatus TechnicalStatus(string paDocumentId, string code, string message) => new()
    {
        PaDocumentId = paDocumentId,
        State = PaSendState.TechnicalError,
        Errors = [new PaError(code, message)],
    };

    // Lectures dont l'endpoint Super PDP n'est PAS confirmé (tax reports, réglage, compte — F14 §3.5, O2) :
    // la capacité SupportsTaxReportRetrieval est false (F14 §5) et le consommateur produit est gaté par
    // elle. Lever une exception traçable est plus sûr que renvoyer une donnée fiscale fausse depuis un
    // endpoint deviné (liste vide = « aucun tax report » serait un MENSONGE fiscal — CLAUDE.md n°3). PAS03
    // confirme ces endpoints en sandbox PUIS bascule la capacité à true.
    private static NotImplementedException NotYetConfirmedInSandbox(string method) =>
        new($"SuperPdp.{method} sera confirmé en sandbox et livré par PAS03 (voir orchestration/items/PAS.yaml, " +
            "F14 §3.5/§8). PAS02 ne livre que l'émission B2C et la relecture d'état (polling).");

    // ── Helpers privés d'INSTANCE ──

    // Envoie une requête AVEC le jeton OAuth bearer ; sur 401, redemande un jeton (le précédent est
    // peut-être expiré/révoqué) et retente UNE fois (F14 §3.1). Le second 401 est classé erreur d'auth
    // re-tentable par le mapper. La requête est reconstruite à chaque tentative (HttpRequestMessage est à
    // usage unique). La réponse vivante est rendue à l'appelant (qui la dispose).
    private async Task<HttpResponseMessage> SendWithAuthAsync(Func<HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var response = await SendOnceAsync(build, token, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var refreshed = await _tokenProvider.GetAccessTokenAsync(forceRefresh: true, cancellationToken).ConfigureAwait(false);
        return await SendOnceAsync(build, refreshed, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        Func<HttpRequestMessage> build,
        string bearer,
        CancellationToken cancellationToken)
    {
        using var request = build();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    // Exécute UNE tentative de POST (auth incluse) et classe le résultat. Distingue le TRANSITOIRE (5xx,
    // réseau, timeout → re-tentable, F14 §4.1) du terminal (émis / rejet métier / auth) pour piloter la
    // boucle d'idempotence. Une erreur d'obtention du jeton OAuth (réseau/non-2xx) lève une
    // HttpRequestException → classée transitoire ici (re-tentable au prochain run).
    private async Task<PostOutcome> TryPostAsync(string url, string json, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendWithAuthAsync(
                () => new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                },
                cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = SuperPdpResponseMapper.MapSendResult(response.StatusCode, body);
            return new PostOutcome(result, SuperPdpResponseMapper.IsRetryableStatus(response.StatusCode));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PostOutcome(
                PaSendResult.Technical(
                    [new PaError("SPDP_TIMEOUT", "Délai d'attente dépassé lors de l'appel Super PDP (re-tentable).")]),
                IsTransient: true);
        }
        catch (HttpRequestException ex)
        {
            return new PostOutcome(
                PaSendResult.Technical(
                    [new PaError("SPDP_NETWORK", $"Erreur réseau Super PDP (re-tentable) : {ex.Message}")]),
                IsTransient: true);
        }
    }

    // Relecture d'idempotence (F14 §4.1) : relit la liste des factures du compte pour RACCROCHER une
    // facture qui aurait DÉJÀ été créée par la tentative qui a échoué (cas du timeout : « émis ou pas ? »).
    //   Found    → facture présente dans la liste lue → on raccroche son état réel ;
    //   NotFound → tout le reste (non-200, forme illisible, échec réseau, OU numéro absent d'une page
    //              potentiellement INCOMPLÈTE) → on NE raccroche PAS et on NE ré-émet PAS.
    // « Numéro absent » n'est PAS « facture absente » tant que la forme de la liste n'est pas confirmée en
    // sandbox (PAS03) : ré-émettre sur cette base risquerait un doublon fiscal (CLAUDE.md n°3).
    private async Task<ReconnectOutcome> TryReconnectByNumberAsync(string number, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return ReconnectOutcome.NotFound;
        }

        var url = SendUrl();
        try
        {
            using var response = await SendWithAuthAsync(
                () => new HttpRequestMessage(HttpMethod.Get, url),
                cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode
                || !SuperPdpResponseMapper.TryParseInvoiceList(body, out var invoices))
            {
                return ReconnectOutcome.NotFound;
            }

            var match = invoices.FirstOrDefault(i => string.Equals(i.Number, number, StringComparison.Ordinal));
            return match is null
                ? ReconnectOutcome.NotFound
                : ReconnectOutcome.AsFound(SuperPdpResponseMapper.MapReconnected(match, body));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ReconnectOutcome.NotFound;
        }
        catch (HttpRequestException)
        {
            return ReconnectOutcome.NotFound;
        }
    }

    // Exécute UNE tentative de relecture d'état (GET /v1.beta/invoices/{id}, auth incluse). Comme pour
    // l'émission, seul le 5xx/réseau/timeout est transitoire ; le mapper classe la réponse finale.
    private async Task<StatusOutcome> TryReadStatusAsync(
        string url,
        string paDocumentId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendWithAuthAsync(
                () => new HttpRequestMessage(HttpMethod.Get, url),
                cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var status = SuperPdpResponseMapper.MapDocumentStatus(response.StatusCode, body, paDocumentId);
            return new StatusOutcome(status, SuperPdpResponseMapper.IsRetryableStatus(response.StatusCode));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new StatusOutcome(
                TechnicalStatus(paDocumentId, "SPDP_TIMEOUT", "Délai d'attente dépassé lors de la relecture Super PDP (re-tentable)."),
                IsTransient: true);
        }
        catch (HttpRequestException ex)
        {
            return new StatusOutcome(
                TechnicalStatus(paDocumentId, "SPDP_NETWORK", $"Erreur réseau Super PDP à la relecture (re-tentable) : {ex.Message}"),
                IsTransient: true);
        }
    }

    // ── Types imbriqués (après toutes les méthodes — ordre StyleCop) ──
    private readonly record struct PostOutcome(PaSendResult Result, bool IsTransient);

    private readonly record struct StatusOutcome(PaDocumentStatus Status, bool IsTransient);

    private readonly record struct ReconnectOutcome(bool Found, PaSendResult? Result)
    {
        public static ReconnectOutcome NotFound => new(Found: false, Result: null);

        public static ReconnectOutcome AsFound(PaSendResult result) => new(Found: true, result);
    }
}
