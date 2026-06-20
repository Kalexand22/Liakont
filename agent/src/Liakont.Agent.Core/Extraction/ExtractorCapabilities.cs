namespace Liakont.Agent.Core.Extraction;

/// <summary>
/// Capacités DÉCLARÉES d'un extracteur source (ADR-0004 D2 — symétrique de <c>PaCapabilities</c>).
/// Chaque adaptateur déclare ce que SA source sait fournir ; la plateforme (Validation, Ingestion,
/// Transmission) s'adapte aux capacités déclarées — JAMAIS par <c>if (source is NAV)</c>. L'agent ne
/// fait que transporter ces capacités (aucune logique métier — CLAUDE.md n°6).
/// </summary>
public sealed class ExtractorCapabilities
{
    /// <summary>Crée un jeu de capacités. Tous les paramètres ont une valeur par défaut SÛRE (capacité absente / forme la plus simple).</summary>
    /// <param name="providesSourceDocuments">La source fournit des PDF liés aux documents (pièces jointes).</param>
    /// <param name="providesUnlinkedDocumentPool">La source fournit un vrac de PDF non liés (réconciliation).</param>
    /// <param name="hasDetailedLines">La source porte des lignes détaillées (sinon : lignes synthétiques par taux).</param>
    /// <param name="hasCreditNoteLink">L'avoir référence sa facture d'origine de façon fiable.</param>
    /// <param name="exposesPayments">La source expose des encaissements datés (F09).</param>
    /// <param name="regimeKeyShape">Forme de la clé de régime TVA par ligne.</param>
    /// <param name="emitterIdentitySource">Origine de l'identité de l'émetteur.</param>
    /// <param name="hasStoredHeaderTotal">Un total d'entête stocké et réconciliable existe.</param>
    /// <param name="isMutableAfterIssue">La source autorise la modification d'un document émis (impacte l'idempotence R2).</param>
    /// <param name="numberUniquenessScope">Granularité d'unicité du numéro de document.</param>
    /// <param name="extractsOnlyFinalizedDocuments">L'adaptateur s'ENGAGE à n'extraire que les documents finalisés/comptabilisés (gate « document finalisé » R9 — ADR-0004 D4 Famille 2). Défaut <c>false</c> = engagement NON déclaré → garde plateforme fail-closed (CLAUDE.md n°3).</param>
    /// <param name="flowKind">Nature du flux de la source (facture unitaire vs e-reporting agrégé — ADR-0004 D4 Famille 3 / §5, RD406). Slot RÉSERVÉ : défaut sûr <see cref="FlowKind.UnitInvoice"/> (forme la plus simple) ; inerte en V1, aucun consommateur plateforme.</param>
    public ExtractorCapabilities(
        bool providesSourceDocuments = false,
        bool providesUnlinkedDocumentPool = false,
        bool hasDetailedLines = false,
        bool hasCreditNoteLink = false,
        bool exposesPayments = false,
        RegimeKeyShape regimeKeyShape = RegimeKeyShape.Simple,
        EmitterIdentitySource emitterIdentitySource = EmitterIdentitySource.FromConfig,
        bool hasStoredHeaderTotal = false,
        bool isMutableAfterIssue = false,
        NumberUniquenessScope numberUniquenessScope = NumberUniquenessScope.Global,
        bool extractsOnlyFinalizedDocuments = false,
        FlowKind flowKind = FlowKind.UnitInvoice)
    {
        ProvidesSourceDocuments = providesSourceDocuments;
        ProvidesUnlinkedDocumentPool = providesUnlinkedDocumentPool;
        HasDetailedLines = hasDetailedLines;
        HasCreditNoteLink = hasCreditNoteLink;
        ExposesPayments = exposesPayments;
        RegimeKeyShape = regimeKeyShape;
        EmitterIdentitySource = emitterIdentitySource;
        HasStoredHeaderTotal = hasStoredHeaderTotal;
        IsMutableAfterIssue = isMutableAfterIssue;
        NumberUniquenessScope = numberUniquenessScope;
        ExtractsOnlyFinalizedDocuments = extractsOnlyFinalizedDocuments;
        FlowKind = flowKind;
    }

    /// <summary>La source fournit des PDF liés aux documents (capacité pièces jointes).</summary>
    public bool ProvidesSourceDocuments { get; }

    /// <summary>La source fournit un vrac de PDF non liés (pool, réconciliation).</summary>
    public bool ProvidesUnlinkedDocumentPool { get; }

    /// <summary>La source porte des lignes détaillées (sinon : lignes synthétiques par taux).</summary>
    public bool HasDetailedLines { get; }

    /// <summary>L'avoir référence sa facture d'origine de façon fiable.</summary>
    public bool HasCreditNoteLink { get; }

    /// <summary>La source expose des encaissements datés (F09).</summary>
    public bool ExposesPayments { get; }

    /// <summary>Forme de la clé de régime TVA par ligne.</summary>
    public RegimeKeyShape RegimeKeyShape { get; }

    /// <summary>Origine de l'identité de l'émetteur.</summary>
    public EmitterIdentitySource EmitterIdentitySource { get; }

    /// <summary>Un total d'entête stocké et réconciliable existe.</summary>
    public bool HasStoredHeaderTotal { get; }

    /// <summary>La source autorise la modification d'un document émis (impacte l'idempotence R2).</summary>
    public bool IsMutableAfterIssue { get; }

    /// <summary>Granularité d'unicité du numéro de document.</summary>
    public NumberUniquenessScope NumberUniquenessScope { get; }

    /// <summary>
    /// L'adaptateur s'ENGAGE à n'extraire que les documents finalisés/comptabilisés — gate « document
    /// finalisé » (R9 du contrat <c>IExtractor</c> ; ADR-0004 D4 Famille 2, P1). Un brouillon ou un
    /// document non comptabilisé ne doit JAMAIS apparaître dans le flux (sinon donnée fiscale fausse —
    /// CLAUDE.md n°3). Défaut <c>false</c> = engagement non déclaré → une garde plateforme fail-closed
    /// (différée, RD403) bloque plutôt que d'envoyer un éventuel brouillon. Drapeau déclaratif de
    /// conformité (PAS de logique métier dans l'agent — CLAUDE.md n°6) ; chaque adaptateur porte la
    /// connaissance de ce qu'est « finalisé » dans SA source.
    /// </summary>
    public bool ExtractsOnlyFinalizedDocuments { get; }

    /// <summary>
    /// Nature du flux de la source — facture unitaire vs e-reporting agrégé (ADR-0004 D4 Famille 3 / §5,
    /// RD406). Slot de capacité RÉSERVÉ : défaut sûr <see cref="FlowKind.UnitInvoice"/> (forme la plus
    /// simple — chaque document est une facture unitaire). INERTE en V1 (aucun consommateur plateforme) ;
    /// réservé pour qu'un futur connecteur POS B2C agrégé soit un ajout, jamais une rupture. L'agent ne
    /// DÉCLARE que la forme observée, il ne l'interprète pas (CLAUDE.md n°6).
    /// </summary>
    public FlowKind FlowKind { get; }
}
