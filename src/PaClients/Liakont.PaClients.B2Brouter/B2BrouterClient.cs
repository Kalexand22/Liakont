namespace Liakont.PaClients.B2Brouter;

using System.Text;
using System.Text.Json;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.B2Brouter.Wire;

/// <summary>
/// Plug-in PA B2Brouter (eDocExchange) — implémentation d'<see cref="IPaClient"/> (F05). Encapsule
/// TOUTES les interactions B2Brouter (URLs, en-têtes, format JSON) : aucun autre composant ne connaît
/// ces détails (F05 §1), c'est ce qui rend le produit indépendant de toute PA (blueprint.md §2 ;
/// CLAUDE.md n°6). Le type est <c>internal</c> : il ne fuit pas hors de l'assembly (acceptance PAB01)
/// — la fabrique le rend derrière l'abstraction <see cref="IPaClient"/>.
/// <para>
/// PÉRIMÈTRE PAB01+PAB02 = l'ENVOI de document (<see cref="SendDocumentAsync"/> : auth, transformation
/// pivot → JSON, POST) AVEC la gestion fine des 3 familles d'erreurs de F05 §4.1 (retry/backoff sur le
/// transitoire, pas de retry sur les rejets métier ni l'auth) et l'idempotence anti-doublon de F05 §4.2
/// (relecture de la liste du compte avant tout re-POST), plus la relecture d'état
/// (<see cref="GetDocumentStatusAsync"/>). Les tax reports, le réglage et la facture générée sont livrés
/// par PAB03. Les capacités déclarées (<see cref="B2BrouterCapabilities"/>) reflètent CE périmètre : un
/// appel non encore livré dont la capacité est déclarée <c>false</c> dégrade en résultat TYPÉ (jamais
/// d'exception, jamais de blocage produit — invariant PAA01) ou lève une
/// <see cref="System.NotImplementedException"/> traçable pour les lectures livrées plus tard.
/// </para>
/// </summary>
internal sealed class B2BrouterClient : IPaClient
{
    private readonly HttpClient _httpClient;
    private readonly B2BrouterClientOptions _options;

    /// <summary>
    /// Construit le client pour UN compte. Le <paramref name="httpClient"/> est déjà configuré par la
    /// fabrique (URL de base, en-têtes <c>X-B2B-API-Key</c> / <c>X-B2B-API-Version</c>, délai) : le
    /// client ne manipule jamais la clé en clair (CLAUDE.md n°10).
    /// </summary>
    /// <param name="httpClient">Client HTTP configuré pour le compte (URL de base + en-têtes d'auth).</param>
    /// <param name="options">Configuration non sensible du client (compte + capacités).</param>
    public B2BrouterClient(HttpClient httpClient, B2BrouterClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = httpClient;
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

        // Avoir demandé alors que la capacité n'est pas déclarée → résultat typé, jamais d'exception
        // ni de blocage produit (invariant PAA01). Détection « avoir » = présence d'une référence
        // d'origine (la classification facture/avoir vit dans Validation — ADR-0004 D3-3).
        if (document.CreditNoteRefs.Count > 0 && !Capabilities.SupportsCreditNotes)
        {
            return PaSendResult.NotSupported(
                PaCapabilityNotSupportedResult.Create(Capabilities.PaName, PaCapability.CreditNotes));
        }

        // Construction + sérialisation du payload AVANT toute tentative HTTP : un document mal formé
        // (ligne multi-ventilation, avoir groupé multi-origine) lève ici, AVANT le premier appel PA —
        // jamais tronqué en silence (CLAUDE.md n°3), jamais envoyé partiellement.
        var payload = B2BrouterPayloadBuilder.Build(document, sendAfterImport);
        var json = JsonSerializer.Serialize(payload, B2BrouterJson.Options);
        var url = $"accounts/{Uri.EscapeDataString(_options.AccountId)}/invoices.json";
        var policy = _options.RetryPolicy;

        // Boucle de retry des erreurs TRANSITOIRES (réseau / 5xx / timeout — F05 §4.1), gardée par
        // l'idempotence (F05 §4.2) : avant CHAQUE re-POST, on relit la liste du compte pour ne JAMAIS
        // recréer un numéro déjà émis (un doublon de facture serait une faute fiscale — CLAUDE.md n°3).
        // Les rejets métier (4xx, 200 + errors[]) et l'authentification (401/403) ne sont JAMAIS
        // ré-essayés : ils retournent immédiatement (F05 §4.1).
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await TryPostAsync(url, json, cancellationToken).ConfigureAwait(false);
            if (!outcome.IsTransient)
            {
                return outcome.Result;
            }

            // Plus de réessai disponible → dégradation en erreur technique re-tentable au prochain run.
            if (attempt >= policy.RetryCount)
            {
                return outcome.Result;
            }

            // Garde d'idempotence avant re-POST (F05 §4.2).
            var reconnect = await TryReconnectByNumberAsync(document.Number, cancellationToken).ConfigureAwait(false);
            if (reconnect.Found)
            {
                // La facture existe déjà côté PA (créée par une tentative précédente) → on raccroche.
                return reconnect.Result!;
            }

            if (!reconnect.Confirmed)
            {
                // Relecture NON CONCLUANTE (liste illisible / en échec) : impossible de garantir l'absence
                // d'un doublon → on n'ose pas re-POSTer, on dégrade en technique re-tentable (le contrôle
                // d'unicité côté Tracking, AVANT l'appel, est la garde primaire — F05 §4.2).
                return outcome.Result;
            }

            // Absence CONFIRMÉE → re-POST sûr après backoff.
            await Task.Delay(policy.Backoffs[attempt], cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<PaSendResult> SendPaymentReportAsync(
        PaymentReportPeriod period,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        cancellationToken.ThrowIfCancellationRequested();

        // B2Brouter ne déclare AUCUN flux de paiement en V1 (flux 10.4/10.2 « planned » — F09, PAB03 §5) :
        // l'appel dégrade en résultat typé piloté par la capacité (jamais d'exception — PAA01).
        if (!Capabilities.SupportsPaymentReport(period.Flux))
        {
            var capability = period.Flux == PaymentReportFlux.Domestic
                ? PaCapability.DomesticPaymentReporting
                : PaCapability.InternationalPaymentReporting;
            return Task.FromResult(PaSendResult.NotSupported(
                PaCapabilityNotSupportedResult.Create(Capabilities.PaName, capability)));
        }

        // Branche « flux supporté » : à implémenter si une version future de B2Brouter active le
        // reporting de paiement (seule la déclaration de capacité changera alors — PAB03 §5).
        throw NotYetImplemented(nameof(SendPaymentReportAsync), "PAB03");
    }

    /// <inheritdoc />
    public async Task<PaDocumentStatus> GetDocumentStatusAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paDocumentId);
        cancellationToken.ThrowIfCancellationRequested();

        // Relecture d'état : GET /invoices/{id}.json (F05 §3). Une lecture est NATURELLEMENT idempotente
        // → simple retry sur le transitoire (réseau / 5xx / timeout), sans garde anti-doublon (rien n'est
        // créé). Les rejets (4xx) et l'auth (401/403) ne sont pas ré-essayés (F05 §4.1).
        var url = $"invoices/{Uri.EscapeDataString(paDocumentId)}.json";
        var policy = _options.RetryPolicy;
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await TryReadStatusAsync(url, paDocumentId, cancellationToken).ConfigureAwait(false);

            // Terminal (état métier, rejet, auth) OU réessais épuisés → on retourne le statut classé
            // (re-tentable au prochain run si transitoire — F05 §4.1).
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
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Récupération des tax reports DGFiP : livrée par PAB03 (capacité SupportsTaxReportRetrieval).
        throw NotYetImplemented(nameof(ListTaxReportsAsync), "PAB03");
    }

    /// <inheritdoc />
    public Task<PaTaxReport> GetTaxReportAsync(
        string taxReportId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taxReportId);
        cancellationToken.ThrowIfCancellationRequested();
        throw NotYetImplemented(nameof(GetTaxReportAsync), "PAB03");
    }

    /// <inheritdoc />
    public Task<PaAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw NotYetImplemented(nameof(GetAccountInfoAsync), "PAB03");
    }

    /// <inheritdoc />
    public Task<PaTaxReportSetting> GetTaxReportSettingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw NotYetImplemented(nameof(GetTaxReportSettingAsync), "PAB03");
    }

    /// <inheritdoc />
    public Task EnsureTaxReportSettingAsync(
        PaTaxReportSettingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Réglage idempotent du tax report (GET puis POST/PATCH si écart) : livré par PAB03.
        throw NotYetImplemented(nameof(EnsureTaxReportSettingAsync), "PAB03");
    }

    /// <inheritdoc />
    public Task<PaGeneratedDocument> GetGeneratedDocumentAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paDocumentId);
        cancellationToken.ThrowIfCancellationRequested();

        // Capacité à vérifier en staging (PAB03 §4) : déclarée false ici → résultat typé, jamais
        // d'exception ni de blocage produit (invariant PAA01).
        if (!Capabilities.SupportsDocumentRetrieval)
        {
            return Task.FromResult(PaGeneratedDocument.NotSupported(
                PaCapabilityNotSupportedResult.Create(Capabilities.PaName, PaCapability.DocumentRetrieval)));
        }

        throw NotYetImplemented(nameof(GetGeneratedDocumentAsync), "PAB03");
    }

    // ── Helpers privés STATIQUES (avant les helpers d'instance — ordre StyleCop) ──
    private static PaDocumentStatus TechnicalStatus(string paDocumentId, string code, string message) => new()
    {
        PaDocumentId = paDocumentId,
        State = PaSendState.TechnicalError,
        Errors = [new PaError(code, message)],
    };

    // Marqueur d'incrément traçable : ces lectures appartiennent à un item PAB ultérieur (même branche,
    // séquentiel) ; aucun consommateur produit ne les appelle encore (pipeline PIP et câblage Host non
    // livrés). À DISTINGUER d'un gap de capacité PERMANENT (paiement, document généré) qui, lui, retourne
    // un résultat TYPÉ via les fabriques dédiées de PaSendResult / PaGeneratedDocument (invariant PAA01).
    // Ici la capacité est false parce que la FONCTION n'est pas encore construite (PAB03 la livre PUIS
    // bascule la capacité à true) : lever une exception loud est plus sûr que retourner un faux résultat
    // bénin (liste vide = « aucun tax report » serait un MENSONGE fiscal, un faux-vert — CLAUDE.md n°3).
    private static System.NotImplementedException NotYetImplemented(string method, string item) =>
        new($"B2Brouter.{method} sera livré par {item} (voir orchestration/items/PAB.yaml). " +
            "PAB01 ne livre que l'envoi de document (SendDocumentAsync).");

    // ── Helpers privés d'INSTANCE ──
    // Exécute UNE tentative de POST et classe le résultat. Distingue le TRANSITOIRE (5xx, réseau,
    // timeout → re-tentable, F05 §4.1) du terminal (émis / rejet métier / auth) pour piloter la boucle.
    private async Task<PostOutcome> TryPostAsync(string url, string json, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = B2BrouterResponseMapper.MapSendResult(response.StatusCode, body);

            // Seuls les 5xx sont transitoires côté HTTP : 401/403 (auth) et 4xx (rejet) ne se retentent
            // pas (F05 §4.1) — ils sortent immédiatement de la boucle.
            return new PostOutcome(result, B2BrouterResponseMapper.IsRetryableStatus(response.StatusCode));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Délai d'attente HTTP (F05 §4.3) — transitoire re-tentable, pas une annulation appelant.
            return new PostOutcome(
                PaSendResult.Technical(
                    [new PaError("B2B_TIMEOUT", "Délai d'attente dépassé lors de l'appel B2Brouter (re-tentable).")]),
                IsTransient: true);
        }
        catch (HttpRequestException ex)
        {
            // Réseau / DNS / coupure → transitoire re-tentable au prochain run (F05 §4.1).
            return new PostOutcome(
                PaSendResult.Technical(
                    [new PaError("B2B_NETWORK", $"Erreur réseau B2Brouter (re-tentable) : {ex.Message}")]),
                IsTransient: true);
        }
    }

    // Garde d'idempotence (F05 §4.2) : relit la liste des factures du compte pour savoir si le numéro a
    // DÉJÀ été créé par une tentative précédente (cas du timeout : « envoyé ou pas ? »). Trois issues :
    //   Found       → la facture existe → on raccroche son état (pas de re-POST) ;
    //   Absent      → liste lue et numéro ABSENT → re-POST sûr ;
    //   non Confirmé → relecture illisible/en échec → NON CONCLUANT : on ne re-POSTe pas (anti-doublon).
    private async Task<ReconnectOutcome> TryReconnectByNumberAsync(string number, CancellationToken cancellationToken)
    {
        // Sans numéro fiable, l'idempotence est impossible à garantir → non concluant (jamais de re-POST
        // à l'aveugle). Un document sans numéro ne devrait pas arriver ici (validation amont).
        if (string.IsNullOrWhiteSpace(number))
        {
            return ReconnectOutcome.Inconclusive;
        }

        var url = $"accounts/{Uri.EscapeDataString(_options.AccountId)}/invoices.json";
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // Toute relecture non-200 ou de forme inconnue est NON CONCLUANTE : on préfère re-tenter au
            // prochain run plutôt que risquer un doublon (CLAUDE.md n°3).
            if (!response.IsSuccessStatusCode
                || !B2BrouterResponseMapper.TryParseInvoiceList(body, out var invoices))
            {
                return ReconnectOutcome.Inconclusive;
            }

            var match = invoices.FirstOrDefault(i => string.Equals(i.Number, number, StringComparison.Ordinal));
            return match is null
                ? ReconnectOutcome.Absent
                : ReconnectOutcome.AsFound(B2BrouterResponseMapper.MapReconnected(match, body));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ReconnectOutcome.Inconclusive;
        }
        catch (HttpRequestException)
        {
            return ReconnectOutcome.Inconclusive;
        }
    }

    // Exécute UNE tentative de relecture d'état (GET /invoices/{id}.json). Comme pour l'envoi, seul le
    // 5xx/réseau/timeout est transitoire ; le mapper classe la réponse finale.
    private async Task<StatusOutcome> TryReadStatusAsync(
        string url,
        string paDocumentId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var status = B2BrouterResponseMapper.MapDocumentStatus(response.StatusCode, body, paDocumentId);
            return new StatusOutcome(status, B2BrouterResponseMapper.IsRetryableStatus(response.StatusCode));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new StatusOutcome(
                TechnicalStatus(paDocumentId, "B2B_TIMEOUT", "Délai d'attente dépassé lors de la relecture B2Brouter (re-tentable)."),
                IsTransient: true);
        }
        catch (HttpRequestException ex)
        {
            return new StatusOutcome(
                TechnicalStatus(paDocumentId, "B2B_NETWORK", $"Erreur réseau B2Brouter à la relecture (re-tentable) : {ex.Message}"),
                IsTransient: true);
        }
    }

    // ── Types imbriqués (après toutes les méthodes — ordre StyleCop) ──
    // Issue d'une tentative de POST : le résultat classé + s'il est re-tentable (transitoire).
    private readonly record struct PostOutcome(PaSendResult Result, bool IsTransient);

    // Issue d'une tentative de relecture d'état : le statut classé + s'il est re-tentable (transitoire).
    private readonly record struct StatusOutcome(PaDocumentStatus Status, bool IsTransient);

    // Issue de la garde d'idempotence. <c>Confirmed</c> = réponse définitive (trouvée ou absente) ;
    // <c>Found</c> = facture retrouvée (alors <c>Result</c> porte l'état raccroché).
    private readonly record struct ReconnectOutcome(bool Confirmed, bool Found, PaSendResult? Result)
    {
        public static ReconnectOutcome Inconclusive => new(Confirmed: false, Found: false, Result: null);

        public static ReconnectOutcome Absent => new(Confirmed: true, Found: false, Result: null);

        public static ReconnectOutcome AsFound(PaSendResult result) => new(Confirmed: true, Found: true, result);
    }
}
