namespace Liakont.Modules.Transmission.Contracts;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Abstraction d'une Plateforme Agréée (PA) — c'est LE produit : Liakont reste indépendant de toute
/// PA concrète (B2Brouter, Super PDP…), qui n'est qu'un plug-in de ce contrat (blueprint.md §2 ;
/// F05 §1). Aucune fonctionnalité du produit ne dépend de ce qu'UNE PA sait faire : le comportement
/// est piloté par les <see cref="Capabilities"/> déclarées, jamais par un <c>if (pa is B2Brouter)</c>
/// (CLAUDE.md n°6/8/16). Quand une capacité manque, l'appel retourne un résultat TYPÉ
/// (<see cref="PaCapabilityNotSupportedResult"/>), JAMAIS une exception et JAMAIS un blocage du
/// produit (acceptance PAA01). Aucun type HTTP ne traverse cette interface (frontière vérifiée par
/// NetArchTest) ; la construction du payload PA-spécifique vit dans le plug-in, pas ici (F05 §6).
/// </summary>
public interface IPaClient
{
    /// <summary>Capacités déclarées de la PA — la seule source de vérité du comportement du produit.</summary>
    PaCapabilities Capabilities { get; }

    /// <summary>
    /// Transmet un document (facture B2C ou avoir) à la PA. Reçoit le pivot ENRICHI (EN 16931 — le
    /// mapping TVA est déjà appliqué par la plateforme) ; le plug-in le transforme vers le format de
    /// la PA. <paramref name="sendAfterImport"/> faux = créé sans envoi (état <c>new</c>, F05 §2).
    /// <para>
    /// <paramref name="projection"/> (MND07) convoie les champs sortants qui ne se dérivent pas tels
    /// quels du pivot — aujourd'hui l'autofacturation sous mandat (type BT-3 = 389 + BT-1 fiscal alloué).
    /// <c>null</c> = document standard (le plug-in projette son type par défaut 380 et le BT-1 = <c>Number</c>
    /// du pivot). Quand elle est présente, le plug-in DOIT projeter <see cref="PaOutboundProjection.DocumentTypeCode"/>
    /// et <see cref="PaOutboundProjection.FiscalNumber"/> ; un 389 n'est passé qu'à une PA dont la capacité
    /// <see cref="PaCapabilities.SupportsSelfBilling"/> est déclarée (garde posée par le pipeline, CLAUDE.md n°8).
    /// </para>
    /// <para>
    /// <paramref name="context"/> (FX07, F16 §6.1) porte un artefact fiscal PRÉ-CONSTRUIT par la
    /// plateforme (le Factur-X scellé), à transmettre tel quel. Les PA de niveau « Pilotage » (Super PDP,
    /// B2Brouter) l'IGNORENT — elles construisent leur payload depuis le pivot ; la PA générique l'EXIGE
    /// (artefact absent → blocage, jamais régénéré dans le plug-in). <c>null</c> = aucun artefact
    /// pré-construit (comportement historique inchangé pour les PA existantes).
    /// </para>
    /// </summary>
    /// <param name="document">Le document pivot enrichi à transmettre (EN 16931, F01-F02 §3.1).</param>
    /// <param name="sendAfterImport">Vrai = créer ET envoyer ; faux = créer sans envoyer.</param>
    /// <param name="projection">Projection sortante (type/BT-1) pour les cas non standard, ou <c>null</c>.</param>
    /// <param name="context">Artefact pré-construit optionnel (FX07) ; <c>null</c> pour les PA qui bâtissent leur payload.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<PaSendResult> SendDocumentAsync(
        PivotDocumentDto document,
        bool sendAfterImport = true,
        PaOutboundProjection? projection = null,
        PaSendContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transmet un e-reporting de paiement pour une période. Le <see cref="PaymentReportPeriod"/>
    /// porte son TYPE DE FLUX (Domestic = flux 10.4 / International = flux 10.2, F01-F02 §1) : le
    /// routage et les schémas DGFiP diffèrent. Une PA qui ne supporte pas le flux demandé retourne
    /// un <see cref="PaCapabilityNotSupportedResult"/> (jamais d'exception).
    /// </summary>
    /// <param name="period">Période + type de flux du reporting de paiement.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<PaSendResult> SendPaymentReportAsync(
        PaymentReportPeriod period,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transmet une transaction d'e-reporting B2C AGRÉGÉE (flux 10.3) — représentation AGNOSTIQUE
    /// <see cref="B2cReportingTransaction"/> que le plug-in projette vers son format de fil. L'agrégation
    /// N→1 (jour × devise × catégorie × rôle) est faite EN AMONT par la plateforme, jamais ici ;
    /// l'idempotence (anti-doublon) est portée par l'appelant — la PA n'expose aucune clé d'idempotence.
    /// <para>
    /// Implémentation PAR DÉFAUT, pilotée par les capacités déclarées (PAA01, CLAUDE.md n°8) : une PA qui ne
    /// déclare pas <see cref="PaCapabilities.SupportsB2cReporting"/> — ou, pour la marge <c>TMA1</c>, la
    /// capacité DISTINCTE <see cref="PaCapabilities.SupportsMarginAmountReporting"/> — retourne un
    /// <see cref="PaCapabilityNotSupportedResult"/> TYPÉ, jamais une exception ni un blocage. Un plug-in qui
    /// SAIT transmettre le B2C SURCHARGE cette méthode.
    /// </para>
    /// </summary>
    /// <param name="transaction">La transaction agrégée à transmettre (montants <see cref="decimal"/>, n°1).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<PaSendResult> SendB2cTransactionAsync(
        B2cReportingTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Capabilities.SupportsB2cReporting)
        {
            return Task.FromResult(PaSendResult.NotSupported(
                PaCapabilityNotSupportedResult.Create(Capabilities.PaName, PaCapability.B2cReporting)));
        }

        // La FORME du montant de marge (cas DGFiP n°33) est gardée par une capacité DISTINCTE : une PA qui
        // déclare le B2C mais pas le report de marge ne transmet pas un TMA1 (gel tant que non confirmée).
        if (transaction.Category == EReportingTransactionCategory.Tma1 && !Capabilities.SupportsMarginAmountReporting)
        {
            return Task.FromResult(PaSendResult.NotSupported(
                PaCapabilityNotSupportedResult.Create(Capabilities.PaName, PaCapability.MarginAmountReporting)));
        }

        // Capacité déclarée mais envoi non surchargé : garde-fou de configuration (jamais atteint en prod —
        // un plug-in qui déclare la capacité DOIT surcharger cette méthode).
        throw new NotSupportedException(
            $"Le plug-in « {Capabilities.PaName} » déclare la capacité de report B2C mais ne surcharge pas SendB2cTransactionAsync.");
    }

    /// <summary>Relit l'état d'un document déjà transmis (état, tax_report_ids, errors — F05 §3).</summary>
    /// <param name="paDocumentId">Identifiant du document côté PA.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<PaDocumentStatus> GetDocumentStatusAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default);

    /// <summary>Liste les tax reports (lecture seule, F05 §2). <paramref name="since"/> nul = tous.</summary>
    /// <remarks>
    /// <paramref name="since"/> est un FILTRE BEST-EFFORT : une PA dont l'API n'expose pas de filtre
    /// date côté serveur (ex. B2Brouter — F05 §2) renvoie la liste COMPLÈTE (jamais moins : ne jamais
    /// sous-déclarer). L'appelant ne doit donc PAS présumer un filtrage exact côté PA — il filtre lui-même
    /// (horodatages DocumentEvent côté plateforme) s'il a besoin d'une synchro incrémentale stricte.
    /// </remarks>
    /// <param name="since">Borne basse facultative (date de génération).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<IReadOnlyList<PaTaxReport>> ListTaxReportsAsync(
        DateTime? since = null,
        CancellationToken cancellationToken = default);

    /// <summary>Récupère un tax report précis (inclut son XML base64 si disponible — F05 §2).</summary>
    /// <param name="taxReportId">Identifiant du tax report côté PA.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<PaTaxReport> GetTaxReportAsync(
        string taxReportId,
        CancellationToken cancellationToken = default);

    /// <summary>Informations de compte : consommation et limites de transactions (F05 §2).</summary>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<PaAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Lit le réglage de tax report du compte (F05 §2, B.3).</summary>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<PaTaxReportSetting> GetTaxReportSettingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// S'assure que le réglage de tax report est conforme à la demande (idempotent : lit puis ne
    /// modifie que s'il y a un écart — F05 §2). N'invente aucune valeur fiscale : la demande vient
    /// du paramétrage du tenant (CFG02), jamais du code (CLAUDE.md n°2/7).
    /// </summary>
    /// <param name="request">Réglage souhaité, issu du paramétrage du tenant.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task EnsureTaxReportSettingAsync(
        PaTaxReportSettingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère la facture électronique générée par la PA (Factur-X PDF/A-3, UBL, CII) pour
    /// l'archivage (TRK05). Si la PA ne le permet pas, le résultat porte un
    /// <see cref="PaCapabilityNotSupportedResult"/> (jamais d'exception) — le produit n'est pas bloqué.
    /// </summary>
    /// <param name="paDocumentId">Identifiant du document côté PA.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<PaGeneratedDocument> GetGeneratedDocumentAsync(
        string paDocumentId,
        CancellationToken cancellationToken = default);
}
