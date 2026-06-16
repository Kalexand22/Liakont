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
/// PÉRIMÈTRE = l'ÉMISSION de facture à destinataire IDENTIFIÉ (<see cref="SendDocumentAsync"/> : auth
/// OAuth bearer, pivot → JSON <c>en16931</c> → conversion CII par Super PDP → POST XML — ✅ contrat
/// confirmé sandbox 2026-06-12, F14 §3.2) AVEC la gestion des 3 familles d'erreurs (F14 §4.1 :
/// transitoire / rejet métier 4xx / ASYNCHRONIE des 200 classée par les <c>events[]</c>) et la relecture
/// d'idempotence anti-doublon (<c>external_id</c>) ; plus la relecture d'état
/// (<see cref="GetDocumentStatusAsync"/> : polling, F14 §3.4). Les capacités déclarées
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
        PaSendContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        // FX07 : Super PDP (niveau Pilotage) IGNORE l'artefact pré-construit (context) — elle convertit le
        // pivot en CII côté PA (capacité SupportsFacturXTransmission = false). Chemin inchangé par FX07.

        // Avoir demandé alors que la capacité n'est pas déclarée → résultat typé, jamais d'exception ni de
        // blocage produit (invariant PAA01). Le modèle d'avoir Super PDP est confirmé en sandbox (PAS03,
        // O7) avant d'activer SupportsCreditNotes — V1 ne déclare PAS la capacité (F14 §5).
        if (document.CreditNoteRefs.Count > 0 && !Capabilities.SupportsCreditNotes)
        {
            return PaSendResult.NotSupported(
                PaCapabilityNotSupportedResult.Create(Capabilities.PaName, PaCapability.CreditNotes));
        }

        // Super PDP n'expose PAS de « création sans envoi » : POST /v1.beta/invoices crée ET met en file
        // d'envoi (✅ confirmé OpenAPI — F14 §3.2). Émettre quand l'appelant demandait une simple création
        // serait une émission fiscale NON VOULUE : résultat typé, jamais d'envoi (CLAUDE.md n°3). Aucun
        // appelant produit n'utilise sendAfterImport=false (défaut du contrat = true).
        if (!sendAfterImport)
        {
            const string noDraftMessage =
                "Super PDP ne propose pas de création sans envoi (F14 §3.2) — demander l'envoi " +
                "(sendAfterImport=true) ou utiliser une autre PA pour préparer un brouillon.";
            return PaSendResult.Rejected([new PaError("SPDP_NO_DRAFT", noDraftMessage)]);
        }

        // L'émission Super PDP exige un destinataire IDENTIFIÉ et ADRESSABLE dans l'annuaire (contrôle
        // serveur « missing buyer electronic address », ✅ constaté sandbox — F14 §3.2) : l'adressage V1
        // passe par le SIREN (scheme 0002). Sans SIREN acheteur, l'envoi est IMPOSSIBLE par ce canal
        // (le B2C anonyme relève de l'e-reporting, hors V1) : rejet local typé AVANT tout appel, message
        // opérateur actionnable (CLAUDE.md n°12) — jamais un envoi voué à l'échec.
        if (string.IsNullOrWhiteSpace(document.Customer?.Siren))
        {
            const string buyerMessage =
                "Super PDP exige un destinataire identifié par SIREN pour émettre une facture " +
                "(adressage annuaire — F14 §3.2). Renseigner le SIREN du destinataire, ou transmettre " +
                "ce document par e-reporting (non couvert par le plug-in Super PDP V1).";
            return PaSendResult.Rejected([new PaError("SPDP_BUYER_NOT_ADDRESSABLE", buyerMessage)]);
        }

        // Même contrôle côté vendeur : la PA vérifie que le vendeur correspond à l'entreprise du compte
        // (✅ constaté sandbox : « L'entreprise (X) liée à cette session ne correspond pas au vendeur (Y) »).
        // Sans SIREN vendeur, ni l'identification (BT-30) ni l'adressage (BT-34) ne sont constructibles.
        if (string.IsNullOrWhiteSpace(document.Supplier.Siren))
        {
            const string sellerMessage =
                "Le SIREN du vendeur est absent du document : l'émission Super PDP exige " +
                "l'identification légale du vendeur (EN 16931 BT-30, scheme 0002 — F14 §3.2). " +
                "Vérifier le profil fiscal du tenant et les données du document.";
            return PaSendResult.Rejected([new PaError("SPDP_SELLER_SIREN_MISSING", sellerMessage)]);
        }

        // Construction + sérialisation du payload AVANT toute tentative HTTP : un document mal formé
        // (ligne sans ventilation ou multi-ventilation, charges document non mappées) lève ici, AVANT le
        // premier appel PA — jamais tronqué en silence (CLAUDE.md n°3), jamais envoyé partiellement.
        var payload = SuperPdpPayloadBuilder.Build(document);
        var json = JsonSerializer.Serialize(payload, SuperPdpJson.Options);

        // Étape 1/2 — conversion en16931 → CII par Super PDP (F14 §3.2). La conversion ne CRÉE rien côté
        // PA : un échec transitoire est re-tentable au prochain run SANS relecture d'idempotence ; un 4xx
        // porte les messages des règles EN 16931 (BR-*), conservés intacts.
        var converted = await TryConvertAsync(json, cancellationToken).ConfigureAwait(false);
        if (converted.Failure is not null)
        {
            return converted.Failure;
        }

        // Étape 2/2 — envoi du XML CII (création côté PA). L'external_id porte le numéro de document
        // (clé d'idempotence, F14 §4.1).
        var externalId = ExternalIdFor(document.Number);
        var outcome = await TryPostInvoiceAsync(converted.Xml!, externalId, cancellationToken).ConfigureAwait(false);
        if (!outcome.IsTransient)
        {
            // Rejet 4xx SANS identifiant : peut être le REFUS ANTI-DOUBLON du serveur — Super PDP refuse de
            // recréer une facture au même numéro (« La facture est déjà existante (id N) », ✅ constaté
            // sandbox 2026-06-12 — F14 §4.1). Avant de figer un rejet, on tente le raccrochage par
            // external_id : trouvée → on rattache son état RÉEL (un document créé classé « rejeté » serait
            // un faux état fiscal) ; sinon le rejet est rendu tel quel, message intact. Un rejet AVEC
            // identifiant (échec asynchrone events[]) est déjà tranché sur la facture existante.
            if (outcome.Result.State == PaSendState.RejectedByPa && outcome.Result.PaDocumentId is null)
            {
                var dedup = await TryReconnectByExternalIdAsync(externalId, cancellationToken).ConfigureAwait(false);
                if (dedup.Found)
                {
                    return dedup.Result!;
                }
            }

            // Terminal : téléversée (Sending — l'envoi est asynchrone), rejet métier (4xx) ou auth
            // (401/403, déjà retentée une fois avec jeton rafraîchi). Aucun ne se re-tente (F14 §4.1).
            return outcome.Result;
        }

        // Erreur TRANSITOIRE (réseau / 5xx / timeout) : on NE ré-émet PAS à l'aveugle. On fait UNE relecture
        // d'idempotence : si la facture existe déjà (external_id), on raccroche son état ; sinon on dégrade
        // en TechnicalError, RE-TENTABLE AU PROCHAIN RUN — un éventuel re-POST d'une facture pourtant créée
        // est alors REFUSÉ par l'anti-doublon serveur (même numéro) et raccroché par le bloc ci-dessus :
        // jamais de double émission (CLAUDE.md n°3). Une liste illisible/incomplète reste NON CONCLUANTE
        // (jamais « facture absente »).
        var reconnect = await TryReconnectByExternalIdAsync(externalId, cancellationToken).ConfigureAwait(false);
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
    public Task<PaTaxReportSetting> GetTaxReportSettingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Lecture du réglage de transmission (« SIREN publié ? ») appelée INCONDITIONNELLEMENT par le
        // diagnostic pré-envoi du pipeline (SendTenantJob, F04 §3.1) — HORS de toute garde de capacité ET
        // HORS du filet SafeProcessAsync : lever ici bloquerait TOUT envoi et planterait le job (invariant
        // PAA01 « jamais un blocage du produit »). L'endpoint Super PDP du réglage n'est PAS confirmé
        // (F14 §3.5, O2) : on retourne un réglage VIDE/INACTIF (IsActiveOn = false) plutôt que de sonder un
        // endpoint deviné. Le SEND dégrade alors proprement en « SIREN non publié / Transport not available »
        // — fail-closed : on NE risque JAMAIS d'émettre depuis un réglage faussement actif (CLAUDE.md n°3) —
        // sans planter. PAS03 livre la lecture réelle contre l'endpoint confirmé en sandbox PUIS bascule
        // cette branche (et la capacité de récupération).
        return Task.FromResult(new PaTaxReportSetting());
    }

    /// <inheritdoc />
    public Task EnsureTaxReportSettingAsync(
        PaTaxReportSettingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Publication du SIREN / activation de la transmission (action opérateur « Publier le SIREN »,
        // PaPublicationConsoleService). N'est gardée par AUCUNE capacité : par convention du contrat
        // (Fake et B2Brouter ne lèvent jamais ici), on NE lève PAS — lever contredirait l'invariant PAA01.
        // L'endpoint Super PDP du réglage n'est PAS confirmé (F14 §3.5, O2) : PAS02 est un NO-OP idempotent.
        // Conséquence cohérente et SÛRE : le réglage ne devient pas actif (GetTaxReportSettingAsync reste
        // VIDE) → le SEND reste correctement bloqué (« SIREN non publié »), le produit n'émet JAMAIS vers un
        // SIREN non publié (CLAUDE.md n°3). PAS03 livre l'écriture idempotente réelle contre l'endpoint
        // confirmé en sandbox.
        return Task.CompletedTask;
    }

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

    // URL de conversion en16931 → CII : /v1.beta/invoices/convert?from=en16931&to=cii (F14 §3.2). Pas
    // d'identifiant de compte dans l'URL : le compte est porté par le jeton OAuth (client credentials).
    private static string ConvertUrl() =>
        $"{SuperPdpDefaults.ApiVersionPrefix}/{SuperPdpDefaults.ConvertPath}" +
        $"?from={SuperPdpDefaults.ConvertFromFormat}&to={SuperPdpDefaults.ConvertToFormat}";

    // URL d'émission : /v1.beta/invoices?external_id=… (clé d'idempotence — F14 §4.1).
    private static string SendUrl(string externalId) =>
        $"{SuperPdpDefaults.ApiVersionPrefix}/{SuperPdpDefaults.InvoicesPath}" +
        $"?external_id={Uri.EscapeDataString(externalId)}";

    // URL de liste (relecture d'idempotence, F14 §4.1) : nos émissions les plus RÉCENTES d'abord
    // (direction=out, order=desc), fenêtre MAXIMALE (limit=1000, le max OpenAPI — la liste n'a pas de
    // filtre external_id), events inclus (expand[]=events : sans lui la liste ne porte pas les événements
    // et le raccrochage classerait « en cours » au lieu de l'état réel). La facture cherchée vient d'être
    // créée (timeout ou refus anti-doublon immédiat) : la fenêtre des 1000 plus récentes la couvre ;
    // au-delà, « absente » reste NON CONCLUANT (pagination par curseur — jamais de ré-émission).
    private static string ListUrl() =>
        $"{SuperPdpDefaults.ApiVersionPrefix}/{SuperPdpDefaults.InvoicesPath}" +
        $"?direction=out&order=desc&limit=1000&{Uri.EscapeDataString("expand[]")}=events";

    // Clé d'idempotence portée par ?external_id= : le numéro de document (BT-1) tel quel tant qu'il tient
    // dans la limite de l'API (36 caractères — ✅ OpenAPI). Au-delà, une empreinte SHA-256 hex tronquée,
    // DÉTERMINISTE (même numéro → même clé, la relecture d'idempotence reste fiable) — jamais une
    // troncature brute qui créerait des collisions entre numéros longs partageant un préfixe.
    private static string ExternalIdFor(string number)
    {
        if (number.Length <= SuperPdpDefaults.ExternalIdMaxLength)
        {
            return number;
        }

        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(number));
        return Convert.ToHexStringLower(hash)[..SuperPdpDefaults.ExternalIdMaxLength];
    }

    // URL de relecture d'état : /v1.beta/invoices/{id} (F14 §3.4).
    private static string StatusUrl(string paDocumentId) =>
        $"{SuperPdpDefaults.ApiVersionPrefix}/{SuperPdpDefaults.InvoicesPath}/{Uri.EscapeDataString(paDocumentId)}";

    private static PaDocumentStatus TechnicalStatus(string paDocumentId, string code, string message) => new()
    {
        PaDocumentId = paDocumentId,
        State = PaSendState.TechnicalError,
        Errors = [new PaError(code, message)],
    };

    // Lectures GARDÉES PAR CAPACITÉ dont l'endpoint Super PDP n'est PAS confirmé (liste/détail tax reports,
    // compte — F14 §3.5, O2) : la capacité SupportsTaxReportRetrieval est false (F14 §5) ET le consommateur
    // produit n'appelle ces lectures QUE sous cette capacité (SyncTenantJob) — lever ici NE bloque donc
    // jamais le produit. À DISTINGUER de GetTaxReportSettingAsync / EnsureTaxReportSettingAsync, qui ne sont
    // gardées par AUCUNE capacité et sont appelées par le chemin d'envoi : celles-là dégradent sans lever
    // (réglage vide + no-op, voir ci-dessus). Pour ces lectures-ci, lever une exception traçable est plus
    // sûr que renvoyer une donnée fiscale fausse depuis un endpoint deviné (liste vide = « aucun tax report »
    // serait un MENSONGE fiscal, sous-déclaration — CLAUDE.md n°3). PAS03 confirme ces endpoints en sandbox
    // PUIS bascule la capacité à true.
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

    // Étape 1/2 de l'émission : conversion en16931 → CII (F14 §3.2). La conversion ne crée RIEN côté PA :
    // un échec transitoire (réseau / 5xx / timeout) dégrade directement en TechnicalError re-tentable au
    // prochain run, SANS relecture d'idempotence. Un 200 rend le XML CII prêt à émettre ; tout autre code
    // est classé par le mapper (messages BR-* intacts). Une erreur d'obtention du jeton OAuth lève une
    // HttpRequestException → classée transitoire ici.
    private async Task<ConvertOutcome> TryConvertAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendWithAuthAsync(
                () => new HttpRequestMessage(HttpMethod.Post, ConvertUrl())
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                },
                cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? ConvertOutcome.Success(body)
                : ConvertOutcome.Failed(SuperPdpResponseMapper.MapConvertFailure(response.StatusCode, body));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ConvertOutcome.Failed(PaSendResult.Technical(
                [new PaError("SPDP_TIMEOUT", "Délai d'attente dépassé lors de la conversion Super PDP (re-tentable).")]));
        }
        catch (HttpRequestException ex)
        {
            return ConvertOutcome.Failed(PaSendResult.Technical(
                [new PaError("SPDP_NETWORK", $"Erreur réseau Super PDP à la conversion (re-tentable) : {ex.Message}")]));
        }
    }

    // Étape 2/2 de l'émission : POST du XML CII (auth incluse). Distingue le TRANSITOIRE (5xx, réseau,
    // timeout → re-tentable, F14 §4.1) du terminal (téléversée / rejet métier / auth) pour piloter la
    // boucle d'idempotence.
    private async Task<PostOutcome> TryPostInvoiceAsync(string xml, string externalId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendWithAuthAsync(
                () => new HttpRequestMessage(HttpMethod.Post, SendUrl(externalId))
                {
                    Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
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
    //   Found    → facture portant NOTRE external_id présente dans la page lue → on raccroche son état ;
    //   NotFound → tout le reste (non-200, forme illisible, échec réseau, OU external_id absent d'une page
    //              potentiellement INCOMPLÈTE) → on NE raccroche PAS et on NE ré-émet PAS.
    // « Absent de la page » n'est PAS « facture absente » (pagination par curseur — F14 §3.2) : ré-émettre
    // sur cette base risquerait un doublon fiscal (CLAUDE.md n°3) ; le résultat reste TechnicalError
    // re-tentable et la relecture re-tentera au prochain run.
    private async Task<ReconnectOutcome> TryReconnectByExternalIdAsync(string externalId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendWithAuthAsync(
                () => new HttpRequestMessage(HttpMethod.Get, ListUrl()),
                cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode
                || !SuperPdpResponseMapper.TryParseInvoiceList(body, out var invoices))
            {
                return ReconnectOutcome.NotFound;
            }

            var match = invoices.FirstOrDefault(i => string.Equals(i.ExternalId, externalId, StringComparison.Ordinal));
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
    private readonly record struct ConvertOutcome(string? Xml, PaSendResult? Failure)
    {
        public static ConvertOutcome Success(string xml) => new(xml, Failure: null);

        public static ConvertOutcome Failed(PaSendResult failure) => new(Xml: null, failure);
    }

    private readonly record struct PostOutcome(PaSendResult Result, bool IsTransient);

    private readonly record struct StatusOutcome(PaDocumentStatus Status, bool IsTransient);

    private readonly record struct ReconnectOutcome(bool Found, PaSendResult? Result)
    {
        public static ReconnectOutcome NotFound => new(Found: false, Result: null);

        public static ReconnectOutcome AsFound(PaSendResult result) => new(Found: true, result);
    }
}
