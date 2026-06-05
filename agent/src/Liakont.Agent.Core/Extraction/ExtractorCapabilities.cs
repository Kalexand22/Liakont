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
        NumberUniquenessScope numberUniquenessScope = NumberUniquenessScope.Global)
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
}
