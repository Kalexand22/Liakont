namespace Liakont.Modules.Transmission.Tests.Unit.TestDoubles;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Double de test minimal d'<see cref="IPaClient"/> piloté par ses <see cref="Capabilities"/>. Il
/// prouve le comportement EXIGÉ par l'abstraction (PAA01) : une capacité absente retourne un résultat
/// TYPÉ <see cref="PaCapabilityNotSupportedResult"/>, jamais une exception. (Le plug-in factice
/// LIVRÉ — succès/rejet/timeout/journal d'appels — est PAA02, hors périmètre de PAA01.)
/// </summary>
internal sealed class StubPaClient : IPaClient
{
    private readonly List<string> _calls = [];

    public StubPaClient(PaCapabilities capabilities)
    {
        Capabilities = capabilities;
    }

    /// <inheritdoc />
    public PaCapabilities Capabilities { get; }

    /// <summary>Journal des appels (preuve que le pipeline peut être audité — utile en assertion).</summary>
    public IReadOnlyList<string> Calls => _calls;

    /// <inheritdoc />
    public Task<PaSendResult> SendDocumentAsync(
        PivotDocumentDto document,
        bool sendAfterImport = true,
        PaOutboundProjection? projection = null,
        PaSendContext? context = null,
        CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(SendDocumentAsync));
        return Task.FromResult(PaSendResult.Issued("PA-DOC-1"));
    }

    /// <inheritdoc />
    public Task<PaSendResult> SendPaymentReportAsync(
        PaymentReportPeriod period,
        CancellationToken cancellationToken = default)
    {
        _calls.Add($"{nameof(SendPaymentReportAsync)}:{period.Flux}");
        if (!Capabilities.SupportsPaymentReport(period.Flux))
        {
            var capability = period.Flux == PaymentReportFlux.Domestic
                ? PaCapability.DomesticPaymentReporting
                : PaCapability.InternationalPaymentReporting;
            return Task.FromResult(
                PaSendResult.NotSupported(PaCapabilityNotSupportedResult.Create(Capabilities.PaName, capability)));
        }

        return Task.FromResult(PaSendResult.Issued("PA-REPORT-1"));
    }

    /// <inheritdoc />
    public Task<PaDocumentStatus> GetDocumentStatusAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(GetDocumentStatusAsync));
        return Task.FromResult(new PaDocumentStatus { PaDocumentId = paDocumentId, State = PaSendState.Issued });
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PaTaxReport>> ListTaxReportsAsync(
        DateTime? since = null,
        CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(ListTaxReportsAsync));
        return Task.FromResult<IReadOnlyList<PaTaxReport>>([]);
    }

    /// <inheritdoc />
    public Task<PaTaxReport> GetTaxReportAsync(
        string taxReportId,
        CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(GetTaxReportAsync));
        return Task.FromResult(new PaTaxReport
        {
            Id = taxReportId,
            Type = "stub",
            State = PaTaxReportState.New,
        });
    }

    /// <inheritdoc />
    public Task<PaAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(GetAccountInfoAsync));
        return Task.FromResult(new PaAccountInfo { AccountId = "stub-account" });
    }

    /// <inheritdoc />
    public Task<PaTaxReportSetting> GetTaxReportSettingAsync(CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(GetTaxReportSettingAsync));
        return Task.FromResult(new PaTaxReportSetting());
    }

    /// <inheritdoc />
    public Task EnsureTaxReportSettingAsync(
        PaTaxReportSettingRequest request,
        CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(EnsureTaxReportSettingAsync));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PaGeneratedDocument> GetGeneratedDocumentAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(GetGeneratedDocumentAsync));
        if (!Capabilities.SupportsDocumentRetrieval)
        {
            return Task.FromResult(
                PaGeneratedDocument.NotSupported(
                    PaCapabilityNotSupportedResult.Create(Capabilities.PaName, PaCapability.DocumentRetrieval)));
        }

        return Task.FromResult(PaGeneratedDocument.Available([1, 2, 3], "Factur-X"));
    }
}
