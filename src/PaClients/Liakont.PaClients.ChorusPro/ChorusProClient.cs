namespace Liakont.PaClients.ChorusPro;

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;

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
    private readonly HttpClient _httpClient;
    private readonly IChorusProTokenProvider _tokenProvider;
    private readonly string _technicalAccountHeader;
    private readonly PaCapabilities _capabilities;

    /// <summary>Construit le client Chorus Pro avec son transport authentifié et ses capacités déclarées.</summary>
    /// <param name="httpClient">Client HTTP nommé (TLS 1.2/1.3, base API + délai configurés par la fabrique).</param>
    /// <param name="tokenProvider">Fournisseur de jeton OAuth2 PISTE (cache + renouvellement, F18 §2.1).</param>
    /// <param name="technicalAccountHeader">
    /// Valeur de l'en-tête <c>cpro-account</c> du compte technique (<c>base64(login:motDePasse)</c>, F18 §2.2),
    /// pré-calculée et constante pour le compte. SECRÈTE (jamais journalisée — CLAUDE.md n°10).
    /// </param>
    /// <param name="capabilities">Capacités déclarées du plug-in (<see cref="ChorusProCapabilities.Declared"/>).</param>
    public ChorusProClient(
        HttpClient httpClient,
        IChorusProTokenProvider tokenProvider,
        string technicalAccountHeader,
        PaCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(technicalAccountHeader);
        ArgumentNullException.ThrowIfNull(capabilities);
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _technicalAccountHeader = technicalAccountHeader;
        _capabilities = capabilities;
    }

    /// <inheritdoc />
    public PaCapabilities Capabilities => _capabilities;

    /// <inheritdoc />
    public async Task<PaSendResult> SendDocumentAsync(
        PivotDocumentDto document,
        bool sendAfterImport = true,
        PaOutboundProjection? projection = null,
        PaSendContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        // Chorus Pro = transport PUR d'un Factur-X DÉJÀ scellé (niveau « Essentiel », F18 §6 ; patron
        // GeneriqueClient). Garde AVANT tout HTTP : artefact (PaSendContext/FX07) absent ou vide → BLOQUÉ,
        // jamais régénéré dans le plug-in (indépendance plug-in, CLAUDE.md n°6), jamais d'émission « à vide »
        // (bloquer plutôt qu'envoyer faux, n°3). Le plug-in ne calcule/n'arrondit AUCUN montant.
        // La capacité SupportsFacturXTransmission est false jusqu'à CP08 : tant qu'elle l'est, la plateforme
        // ne génère pas l'artefact en amont → cette garde bloque le dépôt (fail-closed). Aucune garde de
        // capacité ici (la capacité gate la génération AMONT — patron SuperPdp/Generique).
        var artifact = context?.PreBuiltArtifact ?? default;
        if (artifact.IsEmpty)
        {
            return BlockedMissingArtifact(document.Number);
        }

        // Payload deposerFluxFacture (F18 §3.1) : fichierFlux=base64(artefact), nomFichier, syntaxeFlux,
        // avecSignature=false. idUtilisateurCourant OMIS (cardinalité non verrouillée, F18 §3.2).
        var payload = ChorusProPayloadBuilder.Build(artifact, document.Number);

        try
        {
            using var response = await SendWithAuthAsync(
                () => new HttpRequestMessage(HttpMethod.Post, ChorusProDefaults.DepositPath)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                },
                cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // Dépôt accepté → Sending (PaDocumentId=numeroFluxDepot) ; 4xx → Rejected ; 5xx/401/403 →
            // Technical. JAMAIS Issued au dépôt (A1/D5). RawResponse conservée (corps réponse = sans credential).
            return ChorusProResponseMapper.MapDeposit(response.StatusCode, body);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout : TechnicalError SANS re-POST (idempotence A3/D8 — un re-dépôt à l'aveugle créerait un
            // second flux = double facture, CLAUDE.md n°3). Reprise opérateur au prochain run.
            return PaSendResult.Technical(
                [new PaError("CPRO_TIMEOUT", $"Délai d'attente dépassé lors du dépôt Chorus Pro du document {document.Number} (re-tentable, sans re-dépôt automatique).")],
                rawResponse: "Délai d'attente dépassé (aucun accusé de réception reçu).");
        }
        catch (HttpRequestException ex)
        {
            // Erreur réseau : TechnicalError re-tentable, SANS re-POST. Le message d'exception (ex.Message)
            // ne porte jamais de credential (les secrets sont dans les en-têtes, jamais dans l'URL/corps).
            return PaSendResult.Technical(
                [new PaError("CPRO_NETWORK", $"Erreur réseau lors du dépôt Chorus Pro du document {document.Number} (re-tentable) : {ex.Message}")],
                rawResponse: "Erreur réseau (aucun accusé de réception reçu).");
        }
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
    public Task<PaDocumentStatus> GetDocumentStatusAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paDocumentId);
        cancellationToken.ThrowIfCancellationRequested();

        // Relecture d'état (consulterCR → etatCourantFlux, F18 §4) livrée par CP03+. La lecture n'est
        // gardée par AUCUNE capacité : ne JAMAIS lever (leçon « méthode différée appelée
        // inconditionnellement »). SQUELETTE → fail-closed TechnicalError re-tentable : jamais un état
        // fiscal inventé (Issued / Intégré) depuis un transport non livré (CLAUDE.md n°2/3). Le squelette
        // n'émet rien, donc cette lecture n'est pas atteinte par le pipeline.
        return Task.FromResult(new PaDocumentStatus
        {
            PaDocumentId = paDocumentId,
            State = PaSendState.TechnicalError,
            Errors = [new PaError(
                "CPRO_NOT_IMPLEMENTED",
                "Relecture d'état Chorus Pro non encore implémentée (livrée par CP03). Réessayer une fois le transport disponible.")],
        });
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

    // Artefact Factur-X absent/vide : la plateforme n'a pas fourni le PDF/A-3 scellé (capacité amont
    // SupportsFacturXTransmission non active, ou génération échouée). Le plug-in BLOQUE sans jamais
    // régénérer (CLAUDE.md n°6) ni émettre « à vide » (n°3) : TechnicalError re-tentable, message FR
    // actionnable avec le numéro de document (CLAUDE.md n°12). Patron GeneriqueClient.BlockedMissingArtifact.
    private static PaSendResult BlockedMissingArtifact(string documentNumber)
    {
        var message =
            $"Document {documentNumber} : la plateforme agréée « {ChorusProDefaults.PaName} » exige un "
            + "Factur-X pré-construit, fourni par le pipeline à l'étape d'envoi. Aucun artefact reçu — "
            + "dépôt bloqué, jamais régénéré par le plug-in (CLAUDE.md n°3/6).";

        return PaSendResult.Technical(
            [new PaError("CPRO_ARTEFACT_REQUIS", message)],
            rawResponse: "Factur-X pré-construit requis mais absent.");
    }

    // Lectures de tax reports GARDÉES par PaCapabilities.SupportsTaxReportRetrieval = false chez leurs
    // appelants (SyncTenantJob) : lever ici NE bloque donc jamais le produit. Une exception traçable est
    // plus sûre qu'une liste vide, qui serait un MENSONGE fiscal (sous-déclaration — CLAUDE.md n°3). CP03+
    // livre ces lectures PUIS bascule la capacité à true.
    private static NotImplementedException NotYetImplemented(string method) =>
        new($"ChorusPro.{method} sera livré par CP03+ (voir orchestration/items/CP.yaml, F18 §4/§5). " +
            "Le squelette CP02 ne fournit que la structure du plug-in (capacités toutes false).");
}
