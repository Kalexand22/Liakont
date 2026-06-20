namespace Liakont.PaClients.ChorusPro;

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.ChorusPro.Wire;

/// <summary>
/// Plug-in PA Chorus Pro — implémentation d'<see cref="IPaClient"/> (F18). Encapsulera TOUTES les
/// interactions Chorus Pro / PISTE (URLs, double en-tête OAuth2 + <c>cpro-account</c>, dépôt
/// <c>deposerFluxFacture</c> du Factur-X scellé, relecture <c>consulterCR</c>) : aucun autre composant ne
/// connaît ces détails (blueprint.md §2 ; CLAUDE.md n°6). Le type est <c>internal</c> : il ne fuit pas
/// hors de l'assembly (acceptance CP02) — la fabrique le rend derrière l'abstraction <see cref="IPaClient"/>.
/// <para>
/// SQUELETTE CP02 : les 9 méthodes + <see cref="Capabilities"/> sont présentes mais NON implémentées
/// (transport métier — dépôt <c>deposerFluxFacture</c>, relecture <c>consulterCR</c> — livré par CP04+).
/// Les capacités déclarées sont toutes <c>false</c> (<see cref="ChorusProCapabilities"/>) : tout appel
/// piloté par une capacité dégrade en résultat TYPÉ (jamais d'exception, jamais de blocage produit —
/// invariant PAA01). Les méthodes appelées SANS garde de capacité par le chemin d'envoi (réglage de
/// publication) dégradent fail-closed (vide / no-op) plutôt que de lever — leçon « méthode de contrat
/// différée appelée inconditionnellement ». Les lectures de tax reports, GARDÉES par
/// <see cref="PaCapabilities.SupportsTaxReportRetrieval"/> = <c>false</c> chez leurs appelants, lèvent une
/// <see cref="NotImplementedException"/> traçable plutôt que de renvoyer une donnée fiscale fausse depuis
/// un endpoint non livré (une liste vide serait un mensonge fiscal — CLAUDE.md n°3).
/// </para>
/// <para>
/// CP03 — AUTH PISTE : le client porte le client HTTP nommé (TLS 1.2/1.3), le fournisseur de jeton OAuth2
/// PISTE (<see cref="IChorusProTokenProvider"/>) et la valeur de l'en-tête <c>cpro-account</c> du compte
/// technique. <see cref="SendWithAuthAsync"/> applique la DOUBLE authentification (Bearer PISTE +
/// <c>cpro-account</c>) à CHAQUE requête et retente UNE fois sur <c>401</c> (jeton peut-être expiré/révoqué
/// → re-échange). Les appelants métier (dépôt / relecture) arrivent avec CP04+ ; l'auth est posée et
/// éprouvée dès CP03. Aucun en-tête d'authentification (Bearer, <c>cpro-account</c>) n'est journalisé —
/// base64 n'est PAS du chiffrement (CLAUDE.md n°10).
/// </para>
/// </summary>
internal sealed class ChorusProClient : IPaClient
{
    // Chemin REST RELATIF du service consulterCR (F18 §4), résolu sur la base API du compte
    // (HttpClient.BaseAddress, fournie par le resolver — F18 §3.3 « base absolue jamais en dur »).
    // 🔶 Segment versionné à VERROUILLER au Swagger PISTE courant (F18 §3.3/§10) : valeur de lecture
    // courante, jamais énoncée comme acquise (CLAUDE.md n°2). « consulterCR » est le nom du SERVICE,
    // pas forcément le segment d'URL final — à confirmer au raccordement.
    private const string ConsulterCrPath = "factures/v1/consulterCR";

    private readonly HttpClient _httpClient;
    private readonly IChorusProTokenProvider _tokenProvider;
    private readonly string _technicalAccountHeader;
    private readonly PaCapabilities _capabilities;
    private readonly ChorusProRetryPolicy _retryPolicy;

    /// <summary>Construit le client Chorus Pro avec son transport authentifié et ses capacités déclarées.</summary>
    /// <param name="httpClient">Client HTTP nommé (TLS 1.2/1.3, base API + délai configurés par la fabrique).</param>
    /// <param name="tokenProvider">Fournisseur de jeton OAuth2 PISTE (cache + renouvellement, F18 §2.1).</param>
    /// <param name="technicalAccountHeader">
    /// Valeur de l'en-tête <c>cpro-account</c> du compte technique (<c>base64(login:motDePasse)</c>, F18 §2.2),
    /// pré-calculée et constante pour le compte. SECRÈTE (jamais journalisée — CLAUDE.md n°10).
    /// </param>
    /// <param name="capabilities">Capacités déclarées du plug-in (<see cref="ChorusProCapabilities.Declared"/>).</param>
    /// <param name="retryPolicy">
    /// Politique de retry des lectures transitoires (<c>consulterCR</c>, F18 §4) ; <c>null</c> =
    /// <see cref="ChorusProRetryPolicy.Default"/> (production). Les tests injectent
    /// <see cref="ChorusProRetryPolicy.NoDelay"/> pour ne pas attendre.
    /// </param>
    public ChorusProClient(
        HttpClient httpClient,
        IChorusProTokenProvider tokenProvider,
        string technicalAccountHeader,
        PaCapabilities capabilities,
        ChorusProRetryPolicy? retryPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(technicalAccountHeader);
        ArgumentNullException.ThrowIfNull(capabilities);
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _technicalAccountHeader = technicalAccountHeader;
        _capabilities = capabilities;
        _retryPolicy = retryPolicy ?? ChorusProRetryPolicy.Default;
    }

    /// <inheritdoc />
    public PaCapabilities Capabilities => _capabilities;

    /// <inheritdoc />
    public Task<PaSendResult> SendDocumentAsync(
        PivotDocumentDto document,
        bool sendAfterImport = true,
        PaOutboundProjection? projection = null,
        PaSendContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        // Chorus Pro = transport pur d'un Factur-X DÉJÀ scellé (capacité FacturXTransmission, F18 §6).
        // SQUELETTE : la capacité n'est pas encore déclarée (livrée par CP03) → résultat TYPÉ, jamais
        // d'exception ni de blocage produit (invariant PAA01).
        return Task.FromResult(PaSendResult.NotSupported(
            PaCapabilityNotSupportedResult.Create(_capabilities.PaName, PaCapability.FacturXTransmission)));
    }

    /// <inheritdoc />
    public Task<PaSendResult> SendPaymentReportAsync(
        PaymentReportPeriod period,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        cancellationToken.ThrowIfCancellationRequested();

        // L'e-reporting de paiement est EXCLU du périmètre Chorus Pro (B2G only, décision D2 — F18 §8) :
        // la capacité reste false → résultat TYPÉ piloté par le flux demandé, jamais d'exception (PAA01).
        var capability = period.Flux == PaymentReportFlux.Domestic
            ? PaCapability.DomesticPaymentReporting
            : PaCapability.InternationalPaymentReporting;
        return Task.FromResult(PaSendResult.NotSupported(
            PaCapabilityNotSupportedResult.Create(_capabilities.PaName, capability)));
    }

    /// <inheritdoc />
    public async Task<PaDocumentStatus> GetDocumentStatusAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paDocumentId);
        cancellationToken.ThrowIfCancellationRequested();

        // Relecture d'état (consulterCR → etatCourantFlux, F18 §4) : POST du numeroFluxDepot, classement par
        // ChorusProStatusMapper (Intégré → Issued SEUL, A1/D5 ; valeur inconnue → fail-safe, jamais Issued —
        // CLAUDE.md n°3). La lecture est NATURELLEMENT idempotente (aucune écriture, aucun re-dépôt) → simple
        // retry/backoff sur le transitoire (5xx / réseau / timeout), sans garde anti-doublon (cohérent D8).
        // Gardée par AUCUNE capacité → ne JAMAIS lever (leçon « méthode différée appelée inconditionnellement »,
        // invariant PAA01) : toute défaillance dégrade en TechnicalStatus re-tentable.
        var policy = _retryPolicy;
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await TryReadStatusAsync(paDocumentId, cancellationToken).ConfigureAwait(false);

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
        throw NotYetImplemented(nameof(ListTaxReportsAsync));

    /// <inheritdoc />
    public Task<PaTaxReport> GetTaxReportAsync(
        string taxReportId,
        CancellationToken cancellationToken = default) =>
        throw NotYetImplemented(nameof(GetTaxReportAsync));

    /// <inheritdoc />
    public Task<PaAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default) =>
        throw NotYetImplemented(nameof(GetAccountInfoAsync));

    /// <inheritdoc />
    public Task<PaTaxReportSetting> GetTaxReportSettingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // « SIREN publié ? » — appelée INCONDITIONNELLEMENT par le diagnostic pré-envoi (F04 §3.1), HORS
        // de toute garde de capacité : ne doit JAMAIS lever (planterait le job — PAA01 ; leçon « méthode
        // différée appelée inconditionnellement »). SQUELETTE → réglage VIDE = SIREN non publié
        // (fail-closed) : le SEND reste bloqué proprement tant que CP03+ n'a pas livré la lecture réelle —
        // jamais un faux « actif » (CLAUDE.md n°3).
        return Task.FromResult(new PaTaxReportSetting());
    }

    /// <inheritdoc />
    public Task EnsureTaxReportSettingAsync(
        PaTaxReportSettingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Action opérateur « Publier le SIREN » — appelée HORS de toute garde de capacité : ne doit JAMAIS
        // lever pour une partie différée (PAA01 ; même leçon que GetTaxReportSettingAsync). SQUELETTE →
        // no-op idempotent : la publication réelle (KYC côté espace Chorus Pro) est livrée par CP03+.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PaGeneratedDocument> GetGeneratedDocumentAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paDocumentId);
        cancellationToken.ThrowIfCancellationRequested();

        // Récupération de la facture générée pour l'archivage (TRK05) : capacité DocumentRetrieval false
        // (squelette) → résultat TYPÉ NotSupported, jamais d'exception ni de blocage produit (PAA01).
        return Task.FromResult(PaGeneratedDocument.NotSupported(
            PaCapabilityNotSupportedResult.Create(_capabilities.PaName, PaCapability.DocumentRetrieval)));
    }

    // DOUBLE authentification PISTE + retry 401 (F18 §2). Pose le Bearer PISTE (jeton en cache/renouvelé)
    // ET l'en-tête cpro-account du compte technique sur CHAQUE tentative ; sur 401, redemande un jeton (le
    // précédent est peut-être expiré/révoqué) et retente UNE fois (patron SuperPdpClient.SendWithAuthAsync).
    // Le second 401 sera classé erreur d'auth re-tentable par le mapper de réponse (livré avec CP04+). La
    // requête est reconstruite à chaque tentative (HttpRequestMessage est à usage unique) ; la réponse
    // vivante est rendue à l'appelant (qui la dispose). Aucun en-tête d'auth n'est journalisé (CLAUDE.md
    // n°10). Méthode INTERNE : exercée par les tests dès CP03 (double en-tête + retry) ; les appelants
    // métier (dépôt deposerFluxFacture, relecture consulterCR) l'utiliseront à partir de CP04.
    internal async Task<HttpResponseMessage> SendWithAuthAsync(Func<HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(build);

        async Task<HttpResponseMessage> SendOnceAsync(string bearer)
        {
            using var request = build();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

            // cpro-account : compte technique Chorus Pro, DISTINCT du Bearer PISTE (F18 §2.2). Valeur
            // SECRÈTE (base64 n'est PAS du chiffrement) — posée sur la requête, jamais journalisée.
            request.Headers.Add(ChorusProDefaults.TechnicalAccountHeaderName, _technicalAccountHeader);
            return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var token = await _tokenProvider.GetAccessTokenAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var response = await SendOnceAsync(token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var refreshed = await _tokenProvider.GetAccessTokenAsync(forceRefresh: true, cancellationToken).ConfigureAwait(false);
        return await SendOnceAsync(refreshed).ConfigureAwait(false);
    }

    // Lectures de tax reports GARDÉES par PaCapabilities.SupportsTaxReportRetrieval = false chez leurs
    // appelants (SyncTenantJob) : lever ici NE bloque donc jamais le produit. Une exception traçable est
    // plus sûre qu'une liste vide, qui serait un MENSONGE fiscal (sous-déclaration — CLAUDE.md n°3). CP03+
    // livre ces lectures PUIS bascule la capacité à true.
    private static NotImplementedException NotYetImplemented(string method) =>
        new($"ChorusPro.{method} sera livré par CP03+ (voir orchestration/items/CP.yaml, F18 §4/§5). " +
            "Le squelette CP02 ne fournit que la structure du plug-in (capacités toutes false).");

    // Construit la requête consulterCR : POST du numeroFluxDepot (F18 §4) sur le chemin RELATIF, résolu sur
    // la base API du compte. Aucun montant, aucun secret dans le corps (transport pur — la double auth est
    // posée par SendWithAuthAsync). camelCase fixé par les attributs du DTO (voir Wire/*).
    private static HttpRequestMessage BuildConsulterCrRequest(string numeroFluxDepot)
    {
        var json = JsonSerializer.Serialize(
            new ChorusProConsulterCrRequest { NumeroFluxDepot = numeroFluxDepot }, ChorusProJson.Options);
        return new HttpRequestMessage(HttpMethod.Post, new Uri(ConsulterCrPath, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    // État technique re-tentable (réseau / timeout) : jamais un état fiscal inventé, jamais une exception qui
    // planterait le pipeline (PAA01). Pas de RawResponse (aucun corps obtenu) ; aucun credential dans le message.
    private static PaDocumentStatus TechnicalStatus(string paDocumentId, string code, string message) =>
        new()
        {
            PaDocumentId = paDocumentId,
            State = PaSendState.TechnicalError,
            Errors = [new PaError(code, message)],
        };

    // Exécute UNE tentative de relecture (consulterCR, auth incluse). Seul le 5xx/réseau/timeout est
    // transitoire ; le mapper classe la réponse finale (4xx → Rejected ; 2xx → etatCourantFlux).
    private async Task<StatusOutcome> TryReadStatusAsync(string paDocumentId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendWithAuthAsync(
                () => BuildConsulterCrRequest(paDocumentId), cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var status = ChorusProStatusMapper.MapDocumentStatus(response.StatusCode, body, paDocumentId);
            return new StatusOutcome(status, ChorusProStatusMapper.IsRetryableStatus(response.StatusCode));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new StatusOutcome(
                TechnicalStatus(paDocumentId, "CPRO_TIMEOUT", "Délai d'attente dépassé lors de la relecture Chorus Pro (re-tentable)."),
                IsTransient: true);
        }
        catch (HttpRequestException ex)
        {
            return new StatusOutcome(
                TechnicalStatus(paDocumentId, "CPRO_NETWORK", $"Erreur réseau Chorus Pro à la relecture (re-tentable) : {ex.Message}"),
                IsTransient: true);
        }
    }

    // Résultat d'une tentative de relecture : l'état classé + s'il faut ré-essayer (transitoire).
    private readonly record struct StatusOutcome(PaDocumentStatus Status, bool IsTransient);
}
