namespace Liakont.PaClients.Fake;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Plug-in PA FACTICE en mémoire (PAA02) — un plug-in À PART ENTIÈRE (il se référence, se configure et
/// s'enregistre exactement comme B2Brouter / Super PDP), livré avec le produit : il fait tourner le
/// mode démo hors-ligne (aucun compte PA réel) et sert de double aux tests du pipeline (PIP), de la
/// console (WEB) et de l'API (API). Il est PILOTABLE via <see cref="FakePaClientOptions"/> (capacités
/// + scénario d'envoi) et JOURNALISE ses appels (<see cref="Calls"/>) pour l'assertion. Conformément à
/// l'abstraction (PAA01), une capacité absente retourne un résultat TYPÉ, jamais une exception et
/// jamais un blocage du produit. C'est aussi la preuve vivante que le modèle plug-in fonctionne.
/// </summary>
public sealed class FakePaClient : IPaClient
{
    private readonly FakePaClientOptions _options;
    private readonly List<FakePaCall> _calls = [];

    // Idempotence (F05 ; clé = numéro de document BT-1) : un document déjà ÉMIS n'est jamais ré-émis.
    private readonly Dictionary<string, PaSendResult> _issued = new(StringComparer.Ordinal);

    // Dernier réglage de tax report « assuré » (EnsureTaxReportSettingAsync) — relu par GetTaxReportSettingAsync.
    private PaTaxReportSetting _taxReportSetting = new();

    /// <summary>Construit un plug-in factice avec une configuration donnée (défaut : PA générale, envoi en succès).</summary>
    /// <param name="options">Configuration du plug-in, ou <c>null</c> pour les valeurs par défaut.</param>
    public FakePaClient(FakePaClientOptions? options = null)
    {
        _options = options ?? new FakePaClientOptions();
    }

    /// <inheritdoc />
    public PaCapabilities Capabilities => _options.Capabilities;

    /// <summary>Journal des appels reçus, dans l'ordre — exploitable en assertion (preuve d'audit du pipeline).</summary>
    public IReadOnlyList<FakePaCall> Calls => _calls;

    /// <summary>Numéros des documents EFFECTIVEMENT émis (déduplication idempotente) — preuve « jamais deux fois ».</summary>
    public IReadOnlyCollection<string> IssuedDocumentNumbers => _issued.Keys;

    /// <inheritdoc />
    public Task<PaSendResult> SendDocumentAsync(
        PivotDocumentDto document,
        bool sendAfterImport = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        Record(nameof(SendDocumentAsync), document.Number);

        // Avoir demandé alors que la PA ne le supporte pas → résultat typé, jamais d'exception (PAA01).
        if (document.CreditNoteRefs.Count > 0 && !Capabilities.SupportsCreditNotes)
        {
            return Task.FromResult(NotSupported(PaCapability.CreditNotes));
        }

        var paDocumentId = $"FAKE-{document.Number}";

        // send_after_import = false → créé sans envoi (état New, non facturable — F05 §2).
        if (!sendAfterImport)
        {
            return Task.FromResult(new PaSendResult { State = PaSendState.New, PaDocumentId = paDocumentId });
        }

        // Idempotence : même numéro déjà émis → on retourne le résultat d'origine, sans ré-émettre.
        if (_issued.TryGetValue(document.Number, out var existing))
        {
            return Task.FromResult(existing);
        }

        var result = BuildSendResult(paDocumentId);
        if (result.State == PaSendState.Issued)
        {
            _issued[document.Number] = result;
        }

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<PaSendResult> SendPaymentReportAsync(
        PaymentReportPeriod period,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        cancellationToken.ThrowIfCancellationRequested();
        Record(nameof(SendPaymentReportAsync), period.Flux.ToString());

        // Flux non déclaré (ex. international en V1) → résultat typé, jamais d'exception (PAA01, F01-F02 §1).
        if (!Capabilities.SupportsPaymentReport(period.Flux))
        {
            var capability = period.Flux == PaymentReportFlux.Domestic
                ? PaCapability.DomesticPaymentReporting
                : PaCapability.InternationalPaymentReporting;
            return Task.FromResult(NotSupported(capability));
        }

        var reportId = $"FAKE-REPORT-{period.Flux}-{period.PeriodStart:yyyyMMdd}-{period.PeriodEnd:yyyyMMdd}";
        return Task.FromResult(BuildSendResult(reportId));
    }

    /// <inheritdoc />
    public Task<PaDocumentStatus> GetDocumentStatusAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paDocumentId);
        cancellationToken.ThrowIfCancellationRequested();
        Record(nameof(GetDocumentStatusAsync), paDocumentId);

        var issued = _issued.Values.FirstOrDefault(r => r.PaDocumentId == paDocumentId);
        return Task.FromResult(new PaDocumentStatus
        {
            PaDocumentId = paDocumentId,
            State = issued is not null ? PaSendState.Issued : PaSendState.New,

            // Attribution PAR DOCUMENT (relue par le SYNC) : les tax_report_ids que l'émission a rattachés au document.
            TaxReportIds = issued?.TaxReportIds ?? [],
        });
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PaTaxReport>> ListTaxReportsAsync(
        DateTime? since = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record(nameof(ListTaxReportsAsync), since?.ToString("o"));

        // Liste de COMPTE pilotée par les options (vide par défaut). `since` est un filtre best-effort (F05 §2) :
        // le plug-in factice renvoie la liste complète, l'appelant filtre lui-même (jamais sous-déclarer).
        return Task.FromResult(_options.TaxReports);
    }

    /// <inheritdoc />
    public Task<PaTaxReport> GetTaxReportAsync(
        string taxReportId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taxReportId);
        cancellationToken.ThrowIfCancellationRequested();
        Record(nameof(GetTaxReportAsync), taxReportId);

        var configured = _options.TaxReports.FirstOrDefault(r => string.Equals(r.Id, taxReportId, StringComparison.Ordinal));
        return Task.FromResult(configured ?? new PaTaxReport
        {
            Id = taxReportId,
            Type = "fake",
            State = PaTaxReportState.New,
        });
    }

    /// <inheritdoc />
    public Task<PaAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record(nameof(GetAccountInfoAsync));
        return Task.FromResult(new PaAccountInfo { AccountId = "fake-account" });
    }

    /// <inheritdoc />
    public Task<PaTaxReportSetting> GetTaxReportSettingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record(nameof(GetTaxReportSettingAsync));
        return Task.FromResult(_taxReportSetting);
    }

    /// <inheritdoc />
    public Task EnsureTaxReportSettingAsync(
        PaTaxReportSettingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        Record(nameof(EnsureTaxReportSettingAsync), request.TypeOperation);

        // Idempotent : on reflète la demande (issue du paramétrage tenant, jamais inventée — PAA01/CLAUDE.md n°2).
        _taxReportSetting = new PaTaxReportSetting
        {
            NafCode = request.NafCode,
            StartDate = request.StartDate,
            TypeOperation = request.TypeOperation,
            EnterpriseSize = request.EnterpriseSize,
            CinScheme = request.CinScheme,
        };
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PaGeneratedDocument> GetGeneratedDocumentAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paDocumentId);
        cancellationToken.ThrowIfCancellationRequested();
        Record(nameof(GetGeneratedDocumentAsync), paDocumentId);

        if (!Capabilities.SupportsDocumentRetrieval)
        {
            return Task.FromResult(PaGeneratedDocument.NotSupported(
                PaCapabilityNotSupportedResult.Create(Capabilities.PaName, PaCapability.DocumentRetrieval)));
        }

        // Contenu factice « FAKE » (0x46 0x41 0x4B 0x45) — un binaire stable, suffisant pour l'archivage de test.
        return Task.FromResult(PaGeneratedDocument.Available([0x46, 0x41, 0x4B, 0x45], "Factur-X"));
    }

    private PaSendResult BuildSendResult(string issuedId) => _options.SendScenario switch
    {
        FakePaScenario.Success => PaSendResult.Issued(issuedId, _options.IssuedTaxReportIds, rawResponse: $"{{\"id\":\"{issuedId}\",\"state\":\"issued\"}}"),
        FakePaScenario.Rejected => PaSendResult.Rejected(
            _options.RejectionErrors, rawResponse: "HTTP 422 — rejet métier simulé."),
        FakePaScenario.SilentError => PaSendResult.Rejected(
            _options.RejectionErrors,
            rawResponse: "HTTP 200 — succès HTTP mais errors[] non vide (erreur silencieuse)."),
        FakePaScenario.TechnicalError => PaSendResult.Technical(
            [new PaError("FAKE_5XX", "Erreur technique simulée (5xx).")], rawResponse: "HTTP 503"),
        FakePaScenario.Timeout => PaSendResult.Technical(
            [new PaError("FAKE_TIMEOUT", "Timeout réseau simulé.")]),
        _ => PaSendResult.Issued(issuedId),
    };

    private PaSendResult NotSupported(PaCapability capability) =>
        PaSendResult.NotSupported(PaCapabilityNotSupportedResult.Create(Capabilities.PaName, capability));

    private void Record(string method, string? detail = null) => _calls.Add(new FakePaCall(method, detail));
}
