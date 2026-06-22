namespace Liakont.Agent.Contracts.Transport;

/// <summary>
/// Capacités DÉCLARÉES de la source d'extraction, transportées de l'agent vers la plateforme
/// (ADR-0004 D2 — symétrique de <c>PaCapabilities</c>). Métadonnée de push : l'agent DÉCLARE ce que
/// SA source sait fournir, la plateforme s'y adapte — JAMAIS par <c>if (source is NAV)</c>. L'agent
/// n'interprète rien (CLAUDE.md n°6) ; les formes énumérées (régime, identité émetteur, unicité du
/// numéro) voyagent en valeur BRUTE (nom de l'énumération source), persistées telles quelles côté
/// plateforme — leur interprétation appartient aux consommateurs métier (RD403 et différés RD409).
/// </summary>
public sealed class ExtractorCapabilitiesDto
{
    /// <summary>Crée un jeu de capacités transporté. Tous les paramètres ont une valeur par défaut SÛRE (capacité absente / forme la plus simple).</summary>
    /// <param name="providesSourceDocuments">La source fournit des PDF liés aux documents (pièces jointes).</param>
    /// <param name="providesUnlinkedDocumentPool">La source fournit un vrac de PDF non liés (réconciliation).</param>
    /// <param name="hasDetailedLines">La source porte des lignes détaillées (sinon : lignes synthétiques par taux).</param>
    /// <param name="hasCreditNoteLink">L'avoir référence sa facture d'origine de façon fiable.</param>
    /// <param name="exposesPayments">La source expose des encaissements datés (F09).</param>
    /// <param name="regimeKeyShape">Forme de la clé de régime TVA par ligne (valeur brute de l'énumération source).</param>
    /// <param name="emitterIdentitySource">Origine de l'identité de l'émetteur (valeur brute de l'énumération source).</param>
    /// <param name="hasStoredHeaderTotal">Un total d'entête stocké et réconciliable existe.</param>
    /// <param name="isMutableAfterIssue">La source autorise la modification d'un document émis (impacte l'idempotence R2).</param>
    /// <param name="numberUniquenessScope">Granularité d'unicité du numéro de document (valeur brute de l'énumération source).</param>
    public ExtractorCapabilitiesDto(
        bool providesSourceDocuments = false,
        bool providesUnlinkedDocumentPool = false,
        bool hasDetailedLines = false,
        bool hasCreditNoteLink = false,
        bool exposesPayments = false,
        string? regimeKeyShape = null,
        string? emitterIdentitySource = null,
        bool hasStoredHeaderTotal = false,
        bool isMutableAfterIssue = false,
        string? numberUniquenessScope = null)
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

    /// <summary>La source expose des encaissements datés (F09). Consommé par RD403.</summary>
    public bool ExposesPayments { get; }

    /// <summary>Forme de la clé de régime TVA par ligne (nom brut de l'énumération source ; <c>null</c> si non déclaré).</summary>
    public string? RegimeKeyShape { get; }

    /// <summary>Origine de l'identité de l'émetteur (nom brut de l'énumération source ; <c>null</c> si non déclaré).</summary>
    public string? EmitterIdentitySource { get; }

    /// <summary>Un total d'entête stocké et réconciliable existe.</summary>
    public bool HasStoredHeaderTotal { get; }

    /// <summary>La source autorise la modification d'un document émis (impacte l'idempotence R2). Consommé par RD403.</summary>
    public bool IsMutableAfterIssue { get; }

    /// <summary>Granularité d'unicité du numéro de document (nom brut de l'énumération source ; <c>null</c> si non déclaré).</summary>
    public string? NumberUniquenessScope { get; }
}
