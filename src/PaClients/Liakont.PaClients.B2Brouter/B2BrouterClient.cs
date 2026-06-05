namespace Liakont.PaClients.B2Brouter;

using System.Net;
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
/// PÉRIMÈTRE PAB01 = l'ENVOI de document (<see cref="SendDocumentAsync"/> : auth, transformation
/// pivot → JSON, POST). La gestion fine des 3 familles d'erreurs avec retry/backoff et l'idempotence
/// (relecture) sont ajoutées par PAB02 ; les tax reports, le réglage et la facture générée par PAB03.
/// Les capacités déclarées (<see cref="B2BrouterCapabilities"/>) reflètent CE périmètre : un appel
/// non encore livré dont la capacité est déclarée <c>false</c> dégrade en résultat TYPÉ (jamais
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

        var payload = B2BrouterPayloadBuilder.Build(document, sendAfterImport);
        var json = JsonSerializer.Serialize(payload, B2BrouterJson.Options);
        var url = $"accounts/{Uri.EscapeDataString(_options.AccountId)}/invoices.json";

        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return B2BrouterResponseMapper.MapSendResult(response.StatusCode, body);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Délai d'attente HTTP (F05 §4.3) — erreur technique re-tentable, pas une annulation appelant.
            return PaSendResult.Technical(
                [new PaError("B2B_TIMEOUT", "Délai d'attente dépassé lors de l'appel B2Brouter (re-tentable).")]);
        }
        catch (HttpRequestException ex)
        {
            // Réseau / DNS / coupure → erreur technique re-tentable au prochain run (F05 §4.1).
            return PaSendResult.Technical(
                [new PaError("B2B_NETWORK", $"Erreur réseau B2Brouter (re-tentable) : {ex.Message}")]);
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

        // Branche « flux supporté » : inatteignable tant que la capacité est false (B2Brouter ne déclare
        // AUCUN flux de paiement — flux 10.4/10.2 « planned », F09 ; PAB03 §5). Si une version future de
        // B2Brouter les active, SEULE la déclaration de capacité changera, et cette branche sera
        // implémentée — aucun autre code produit n'est impacté (invariant produit, CLAUDE.md n°8).
        throw new NotSupportedException(
            "Reporting de paiement B2Brouter (flux 10.4/10.2) : capacité non déclarée (F09). Cette branche " +
            "ne s'active que lorsqu'une version future de B2Brouter exposera ces flux.");
    }

    /// <inheritdoc />
    public Task<PaDocumentStatus> GetDocumentStatusAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paDocumentId);
        cancellationToken.ThrowIfCancellationRequested();

        // Relecture d'état + idempotence (relire avant de retenter) : livrées par PAB02 (gestion d'erreurs).
        throw NotYetImplemented(nameof(GetDocumentStatusAsync), "PAB02");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PaTaxReport>> ListTaxReportsAsync(
        DateTime? since = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // F05 §2 : GET /accounts/{id}/tax_reports.json (lecture seule). B2Brouter n'expose AUCUN filtre
        // date documenté sur cet endpoint : `since` n'est donc PAS appliqué côté PA (la plateforme filtre
        // via les horodatages DocumentEvent). On récupère la liste COMPLÈTE — jamais moins : un filtre
        // inventé risquerait de MASQUER des tax reports (sous-déclaration — CLAUDE.md n°2/3).
        var url = $"accounts/{Uri.EscapeDataString(_options.AccountId)}/tax_reports.json";
        var (status, body) = await SendReadAsync(_httpClient, HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);
        EnsureReadSucceeded(status, "liste des tax reports");
        return B2BrouterReadMapper.MapTaxReports(body);
    }

    /// <inheritdoc />
    public async Task<PaTaxReport> GetTaxReportAsync(
        string taxReportId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taxReportId);
        cancellationToken.ThrowIfCancellationRequested();

        // F05 §2 : GET /tax_reports/{id}.json (PAS sous /accounts). Le xml_base64 n'apparaît qu'après
        // génération du ledger (batch ~02:00) ; son ABSENCE = « pas encore généré », pas une erreur
        // (acceptance PAB03) — le mapper laisse XmlBase64 null sans rien signaler.
        var url = $"tax_reports/{Uri.EscapeDataString(taxReportId)}.json";
        var (status, body) = await SendReadAsync(_httpClient, HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);
        EnsureReadSucceeded(status, $"tax report {taxReportId}");
        return B2BrouterReadMapper.MapTaxReport(body, fallbackId: taxReportId);
    }

    /// <inheritdoc />
    public async Task<PaAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // F05 §2 (facturation) : GET /accounts/{id}.json → transactions_count / transactions_limit
        // (suivi de consommation). Champs absents = null (jamais une valeur par défaut qui masquerait
        // une donnée manquante — module-rules §9).
        var url = $"accounts/{Uri.EscapeDataString(_options.AccountId)}.json";
        var (status, body) = await SendReadAsync(_httpClient, HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);
        EnsureReadSucceeded(status, "informations de compte");
        return B2BrouterReadMapper.MapAccountInfo(body, _options.AccountId);
    }

    /// <inheritdoc />
    public async Task<PaTaxReportSetting> GetTaxReportSettingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var url = TaxReportSettingUrl(_options.AccountId);
        var (status, body) = await SendReadAsync(_httpClient, HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);

        // 404 = réglage pas encore créé (F05 §2) → réglage « vide » (tous champs null), PAS une erreur.
        if (status == HttpStatusCode.NotFound)
        {
            return new PaTaxReportSetting();
        }

        EnsureReadSucceeded(status, "réglage tax report");
        return B2BrouterReadMapper.MapTaxReportSetting(body);
    }

    /// <inheritdoc />
    public async Task EnsureTaxReportSettingAsync(
        PaTaxReportSettingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Idempotent (F05 §2) : GET l'état courant, puis POST (création) si absent, PATCH si écart,
        // no-op si déjà conforme. Toutes les valeurs viennent du paramétrage du tenant (CFG02), jamais
        // du code (CLAUDE.md n°2/7).
        var url = TaxReportSettingUrl(_options.AccountId);
        var (status, body) = await SendReadAsync(_httpClient, HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);

        if (status == HttpStatusCode.NotFound)
        {
            await SendSettingWriteAsync(_httpClient, HttpMethod.Post, url, request, cancellationToken).ConfigureAwait(false);
            return;
        }

        EnsureReadSucceeded(status, "réglage tax report");
        var current = B2BrouterReadMapper.MapTaxReportSetting(body);
        if (B2BrouterReadMapper.SettingMatches(current, request))
        {
            // Déjà conforme → AUCUN appel d'écriture (idempotence — F05 §2).
            return;
        }

        await SendSettingWriteAsync(_httpClient, HttpMethod.Patch, url, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<PaGeneratedDocument> GetGeneratedDocumentAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paDocumentId);
        cancellationToken.ThrowIfCancellationRequested();

        // PAB03 §4 : l'endpoint de téléchargement de la facture GÉNÉRÉE par B2Brouter (Factur-X/UBL,
        // « probable GET /invoices/{id} avec format ») n'est PAS confirmé en staging. Faute de
        // vérification (ticket support ouvert), la capacité reste false → résultat TYPÉ NotSupported,
        // jamais d'exception ni de blocage produit (invariant PAA01). Quand l'endpoint sera validé
        // (suite staging PAB04), SEULES la déclaration de capacité et la branche ci-dessous changeront.
        if (!Capabilities.SupportsDocumentRetrieval)
        {
            return Task.FromResult(PaGeneratedDocument.NotSupported(
                PaCapabilityNotSupportedResult.Create(Capabilities.PaName, PaCapability.DocumentRetrieval)));
        }

        // Inatteignable tant que la capacité est false : garde fail-closed contre une activation de la
        // capacité sans implémentation de l'endpoint confirmé (CLAUDE.md n°2/3).
        throw new NotSupportedException(
            "Téléchargement de la facture générée B2Brouter : endpoint non confirmé en staging (F05 ; PAB03 §4). " +
            "La capacité DocumentRetrieval doit rester false tant que le contrat n'est pas validé (suite staging PAB04).");
    }

    // URL du réglage de tax report DGFiP du compte (F05 §2 : tax_report_settings/dgfip.json).
    // Statique (reçoit l'identifiant de compte) : aucune dépendance d'instance, regroupée avec les
    // autres helpers static (SA1204).
    private static string TaxReportSettingUrl(string accountId) =>
        $"accounts/{Uri.EscapeDataString(accountId)}/{B2BrouterDefaults.DgfipTaxReportSettingPath}";

    // Lecture HTTP brute : renvoie (code, corps) sans interpréter — les erreurs réseau/timeout/annulation
    // remontent naturellement (HttpRequestException / TaskCanceledException) pour un échec LOUD re-tentable
    // côté pipeline, jamais masqué en résultat vide (CLAUDE.md n°3).
    private static async Task<(HttpStatusCode Status, string Body)> SendReadAsync(
        HttpClient httpClient,
        HttpMethod method,
        string url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return (response.StatusCode, body);
    }

    // Écriture du réglage : POST (création) ou PATCH (mise à jour), corps { "tax_report_setting": { … } }.
    private static async Task SendSettingWriteAsync(
        HttpClient httpClient,
        HttpMethod method,
        string url,
        PaTaxReportSettingRequest request,
        CancellationToken cancellationToken)
    {
        var payload = B2BrouterReadMapper.ToWire(request);
        var json = JsonSerializer.Serialize(payload, B2BrouterJson.Options);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(method, url) { Content = content };
        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        EnsureReadSucceeded(response.StatusCode, "écriture du réglage tax report");
    }

    // Échec d'une lecture/écriture = erreur LOUD re-tentable : on ne retourne JAMAIS une valeur vide qui
    // ferait passer un échec serveur pour « rien à déclarer » (mensonge fiscal — CLAUDE.md n°3). Le 404
    // du réglage est traité AVANT cet appel (réglage absent ≠ échec), il n'arrive donc jamais ici.
    private static void EnsureReadSucceeded(HttpStatusCode status, string what)
    {
        if ((int)status is >= 200 and <= 299)
        {
            return;
        }

        throw new HttpRequestException(
            $"Appel B2Brouter « {what} » en échec (HTTP {(int)status}) — re-tentable au prochain run.",
            inner: null,
            statusCode: status);
    }

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
}
