namespace Liakont.PaClients.ChorusPro;

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
/// (transport livré par CP03+). Les capacités déclarées sont toutes <c>false</c>
/// (<see cref="ChorusProCapabilities"/>) : tout appel piloté par une capacité dégrade en résultat TYPÉ
/// (jamais d'exception, jamais de blocage produit — invariant PAA01). Les méthodes appelées SANS garde de
/// capacité par le chemin d'envoi (réglage de publication) dégradent fail-closed (vide / no-op) plutôt que
/// de lever — leçon « méthode de contrat différée appelée inconditionnellement ». Les lectures de tax
/// reports, GARDÉES par <see cref="PaCapabilities.SupportsTaxReportRetrieval"/> = <c>false</c> chez leurs
/// appelants, lèvent une <see cref="NotImplementedException"/> traçable plutôt que de renvoyer une donnée
/// fiscale fausse depuis un endpoint non livré (une liste vide serait un mensonge fiscal — CLAUDE.md n°3).
/// </para>
/// </summary>
internal sealed class ChorusProClient : IPaClient
{
    private readonly PaCapabilities _capabilities;

    /// <summary>Construit le client du squelette avec ses capacités déclarées (toutes false en CP02).</summary>
    /// <param name="capabilities">Capacités déclarées du plug-in (<see cref="ChorusProCapabilities.Declared"/>).</param>
    public ChorusProClient(PaCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        _capabilities = capabilities;
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

    // Lectures de tax reports GARDÉES par PaCapabilities.SupportsTaxReportRetrieval = false chez leurs
    // appelants (SyncTenantJob) : lever ici NE bloque donc jamais le produit. Une exception traçable est
    // plus sûre qu'une liste vide, qui serait un MENSONGE fiscal (sous-déclaration — CLAUDE.md n°3). CP03+
    // livre ces lectures PUIS bascule la capacité à true.
    private static NotImplementedException NotYetImplemented(string method) =>
        new($"ChorusPro.{method} sera livré par CP03+ (voir orchestration/items/CP.yaml, F18 §4/§5). " +
            "Le squelette CP02 ne fournit que la structure du plug-in (capacités toutes false).");
}
