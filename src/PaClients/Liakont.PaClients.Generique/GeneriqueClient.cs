namespace Liakont.PaClients.Generique;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Plug-in PA GÉNÉRIQUE de niveau « Essentiel » (F16 §6) : il ne fait que TRANSPORTER un Factur-X DÉJÀ
/// SCELLÉ (produit par la plateforme à l'étape d'envoi) vers un canal de livraison (email avec pièce
/// jointe / dépôt de fichier), via l'abstraction <see cref="IDocumentDeliveryChannel"/> implémentée au
/// Host. Il ne construit AUCUN payload, ne référence ni MailKit ni le module Notification ni FacturX, et
/// n'a ni statut ni cycle de vie — c'est précisément ce qui en fait du « Essentiel », pas du « Pilotage ».
/// Conformément à l'abstraction (PAA01), toute capacité absente dégrade en résultat TYPÉ, jamais une
/// exception et jamais un blocage du produit. Interne : exposé uniquement derrière <see cref="IPaClient"/>.
/// </summary>
internal sealed class GeneriqueClient : IPaClient
{
    private readonly IDocumentDeliveryChannel _channel;
    private readonly GeneriqueAccountConfig _config;

    /// <summary>Construit un client pour UN compte générique de tenant (canal résolu + cible).</summary>
    /// <param name="channel">Canal de livraison Host correspondant au <see cref="GeneriqueAccountConfig.Method"/> du compte.</param>
    /// <param name="config">Configuration résolue du compte (canal, cible, secret SMTP éventuel déchiffré).</param>
    public GeneriqueClient(IDocumentDeliveryChannel channel, GeneriqueAccountConfig config)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(config);
        if (channel.Method != config.Method)
        {
            throw new ArgumentException(
                $"Le canal fourni ({channel.Method}) ne correspond pas au canal du compte ({config.Method}).",
                nameof(channel));
        }

        _channel = channel;
        _config = config;
    }

    /// <inheritdoc />
    public PaCapabilities Capabilities => GeneriqueCapabilities.Value;

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

        // La PA générique (Essentiel) ne fait que TRANSPORTER un Factur-X DÉJÀ SCELLÉ produit par la
        // plateforme à l'étape d'envoi (FX07, F16 §6.1) et passé via le contexte d'envoi étendu
        // (PaSendContext.PreBuiltArtifact). En l'absence d'artefact (contexte nul / octets vides), on BLOQUE :
        // jamais de régénération dans le plug-in (indépendance plug-in, CLAUDE.md n°6), jamais d'émission
        // « à vide » (bloquer plutôt qu'envoyer faux, CLAUDE.md n°3). sendAfterImport n'a pas de sémantique
        // « créer sans envoyer » côté Essentiel (transmission fire-and-forget) : la transmission est inconditionnelle.
        var artifact = context?.PreBuiltArtifact ?? default;
        if (artifact.IsEmpty)
        {
            return Task.FromResult(BlockedMissingArtifact(document.Number));
        }

        return TransmitFacturXAsync(document.Number, artifact, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PaSendResult> SendPaymentReportAsync(
        PaymentReportPeriod period,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        cancellationToken.ThrowIfCancellationRequested();

        // Niveau Essentiel : aucun e-reporting de paiement → résultat typé, jamais d'exception (PAA01).
        var capability = period.Flux == PaymentReportFlux.Domestic
            ? PaCapability.DomesticPaymentReporting
            : PaCapability.InternationalPaymentReporting;
        return Task.FromResult(NotSupported(capability));
    }

    /// <inheritdoc />
    public Task<PaDocumentStatus> GetDocumentStatusAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paDocumentId);
        cancellationToken.ThrowIfCancellationRequested();

        // Transmission « fire-and-forget » sans cycle de vie (Essentiel) : aucun statut à relire. Le pipeline
        // n'appelle pas le SYNC pour cette PA (capacité de récupération déclarée false) ; défensif si appelé.
        return Task.FromResult(new PaDocumentStatus
        {
            PaDocumentId = paDocumentId,
            State = PaSendState.CapabilityNotSupported,
            RawResponse = "Plug-in générique (Essentiel) : transmission sans relecture de statut.",
        });
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PaTaxReport>> ListTaxReportsAsync(
        DateTime? since = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Aucun tax report (Essentiel) — liste vide (jamais sous-déclarer une donnée existante, ici il n'y en a pas).
        return Task.FromResult<IReadOnlyList<PaTaxReport>>([]);
    }

    /// <inheritdoc />
    public Task<PaTaxReport> GetTaxReportAsync(
        string taxReportId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taxReportId);
        cancellationToken.ThrowIfCancellationRequested();

        // Aucun tax report (Essentiel) : retour neutre, jamais d'exception. Le pipeline ne l'appelle pas
        // (capacité de récupération déclarée false) ; défensif si appelé.
        return Task.FromResult(new PaTaxReport
        {
            Id = taxReportId,
            Type = "none",
            State = PaTaxReportState.New,
            RawResponse = "Plug-in générique (Essentiel) : aucun tax report.",
        });
    }

    /// <inheritdoc />
    public Task<PaAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PaAccountInfo { AccountId = GeneriqueDefaults.PaTypeKey });
    }

    /// <inheritdoc />
    public Task<PaTaxReportSetting> GetTaxReportSettingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Aucun réglage de tax report (Essentiel) : DTO neutre (tous champs null), jamais inventé (CLAUDE.md n°2).
        return Task.FromResult(new PaTaxReportSetting());
    }

    /// <inheritdoc />
    public Task EnsureTaxReportSettingAsync(
        PaTaxReportSettingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Aucun réglage de tax report (Essentiel) : sans objet, idempotent (no-op), jamais d'exception.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PaGeneratedDocument> GetGeneratedDocumentAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paDocumentId);
        cancellationToken.ThrowIfCancellationRequested();

        // La PA générique ne RÉCUPÈRE pas la facture (c'est la plateforme qui la GÉNÈRE) → résultat typé.
        return Task.FromResult(PaGeneratedDocument.NotSupported(
            PaCapabilityNotSupportedResult.Create(GeneriqueDefaults.PaName, PaCapability.DocumentRetrieval)));
    }

    /// <summary>
    /// Transmet un Factur-X DÉJÀ SCELLÉ via le canal configuré (email avec pièce jointe / dépôt de
    /// fichier). C'est la voie de transmission RÉELLE du plug-in, CONSOMMÉE par le câblage pipeline FX07
    /// (qui, à l'étape <c>Sending</c>, résout <c>IFacturXBuilder</c>, génère l'artefact puis appelle cette
    /// méthode juste avant la transmission). Le plug-in ne régénère JAMAIS l'artefact (CLAUDE.md n°6) ;
    /// un artefact vide → blocage (CLAUDE.md n°3). L'idempotence par numéro de document (BT-1) est
    /// STRUCTURELLE : le pipeline ne (re)transmet jamais un document déjà émis (machine à états TRK02).
    /// </summary>
    /// <param name="documentNumber">Numéro de document (BT-1) — sert au nom de fichier et à l'id retourné.</param>
    /// <param name="facturX">Octets du Factur-X PDF/A-3 déjà scellé. Vide → blocage.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    internal async Task<PaSendResult> TransmitFacturXAsync(
        string documentNumber,
        ReadOnlyMemory<byte> facturX,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);
        cancellationToken.ThrowIfCancellationRequested();

        if (facturX.IsEmpty)
        {
            return BlockedMissingArtifact(documentNumber);
        }

        var request = new DocumentDeliveryRequest
        {
            Method = _config.Method,
            Target = _config.Target,
            Content = facturX,
            FileName = GeneriqueDefaults.FileNameFor(documentNumber),
            ContentType = GeneriqueDefaults.FacturXContentType,
            Subject = _config.Method == DocumentDeliveryMethod.Email
                ? $"Facture {documentNumber}"
                : null,
            Body = _config.Method == DocumentDeliveryMethod.Email
                ? $"Veuillez trouver ci-joint la facture {documentNumber} au format Factur-X."
                : null,
            SmtpAuth = _config.SmtpAuth,
        };

        await _channel.DeliverAsync(request, cancellationToken).ConfigureAwait(false);

        // Transmis : la trace d'audit (F06) ne porte que des éléments NON sensibles (canal + cible non
        // secrète) ; aucun secret SMTP n'est journalisé (CLAUDE.md n°18).
        return PaSendResult.Issued(
            $"GENERIQUE-{documentNumber}",
            rawResponse: $"Factur-X transmis via {_config.Method} vers « {_config.Target} ».");
    }

    private static PaSendResult BlockedMissingArtifact(string documentNumber)
    {
        string message =
            $"Document {documentNumber} : la plateforme agréée « {GeneriqueDefaults.PaName} » exige un "
            + "Factur-X pré-construit, fourni par le pipeline à l'étape d'envoi. Aucun artefact reçu — "
            + "transmission bloquée, jamais régénérée par le plug-in (CLAUDE.md n°3/6).";

        return PaSendResult.Technical(
            [new PaError("FXG_ARTEFACT_REQUIS", message)],
            rawResponse: "Factur-X pré-construit requis mais absent.");
    }

    private static PaSendResult NotSupported(PaCapability capability) =>
        PaSendResult.NotSupported(PaCapabilityNotSupportedResult.Create(GeneriqueDefaults.PaName, capability));
}
