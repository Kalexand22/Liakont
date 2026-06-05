namespace Liakont.Modules.Pipeline.Infrastructure.Check;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Décision du mapping TVA au CHECK : soit le document est BLOQUÉ (motif agrégé, opérateur), soit il est
/// PRÊT pour la validation avec son pivot enrichi (catégorie/VATEX par ligne) et la version de table appliquée.
/// </summary>
internal sealed record CheckEvaluation
{
    private CheckEvaluation()
    {
    }

    /// <summary>Vrai si le mapping bloque le document.</summary>
    public bool IsBlocked { get; private init; }

    /// <summary>Motif de blocage agrégé (opérateur) ; <c>null</c> si non bloqué.</summary>
    public string? BlockReason { get; private init; }

    /// <summary>Pivot enrichi (catégorie/VATEX) prêt pour la validation ; <c>null</c> si bloqué.</summary>
    public PivotDocumentDto? EnrichedDocument { get; private init; }

    /// <summary>Version de la table de mapping appliquée ; <c>null</c> si bloqué.</summary>
    public string? MappingVersion { get; private init; }

    /// <summary>Crée une décision « bloqué » avec son motif.</summary>
    public static CheckEvaluation Blocked(string reason) => new()
    {
        IsBlocked = true,
        BlockReason = reason,
    };

    /// <summary>Crée une décision « prêt » avec le pivot enrichi et la version de table.</summary>
    public static CheckEvaluation Ready(PivotDocumentDto enrichedDocument, string? mappingVersion) => new()
    {
        IsBlocked = false,
        EnrichedDocument = enrichedDocument,
        MappingVersion = mappingVersion,
    };
}
