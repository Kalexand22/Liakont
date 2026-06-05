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

        // Branche « flux supporté » : à implémenter si une version future de B2Brouter active le
        // reporting de paiement (seule la déclaration de capacité changera alors — PAB03 §5).
        throw NotYetImplemented(nameof(SendPaymentReportAsync), "PAB03");
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

    // Marqueur d'incrément traçable : ces lectures appartiennent à un item PAB ultérieur (même branche,
    // séquentiel). Aucun consommateur produit ne les appelle encore (pipeline PIP et câblage Host non
    // livrés). Préférable à un faux résultat : on bloque plutôt que d'inventer (CLAUDE.md n°3).
    private static System.NotImplementedException NotYetImplemented(string method, string item) =>
        new($"B2Brouter.{method} sera livré par {item} (voir orchestration/items/PAB.yaml). " +
            "PAB01 ne livre que l'envoi de document (SendDocumentAsync).");
}
