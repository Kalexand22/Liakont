namespace Liakont.Modules.Validation.Contracts.Classification;

/// <summary>
/// Classe le type de document SOURCE brut (<c>PivotDocumentDto.SourceDocumentKind</c>) en facture/avoir,
/// d'après la table de correspondance PROPRE AU TENANT (F04 §3.5bis, ADR-0004 D3-3 « la classification
/// facture/avoir vit dans Validation »). La correspondance varie par logiciel source et n'est connue que
/// du déploiement : elle est validée par l'expert-comptable et provisionnée par seed
/// (<c>deployments/&lt;client&gt;/</c>, CLAUDE.md n°7), JAMAIS devinée ni codée en dur (CLAUDE.md n°2).
/// </summary>
/// <remarks>
/// Abstraction symétrique des autres dépendances tenant-scopées d'une règle de validation
/// (<c>IIssuedInvoiceLookup</c>) : la règle dépend du Contracts, jamais d'un module concret. La
/// résolution est scopée par <paramref name="companyId"/> (CLAUDE.md n°9). Détection seule, aucune
/// écriture. L'implémentation par défaut (<c>UnconfiguredSourceDocumentKindClassifier</c>) répond
/// <see cref="SourceDocumentClassification.Unmapped"/> partout tant qu'aucune table tenant n'est
/// provisionnée (substituable par <c>services.Replace</c>, précédent SUP03).
/// </remarks>
public interface ISourceDocumentKindClassifier
{
    /// <summary>
    /// Classe une valeur de type source pour un tenant donné. Retourne
    /// <see cref="SourceDocumentClassification.Unmapped"/> si la valeur est absente/vide ou non
    /// cartographiée par le tenant (on ne devine pas).
    /// </summary>
    /// <param name="companyId">Tenant propriétaire de la table de correspondance (clé d'isolation).</param>
    /// <param name="sourceDocumentKind">Valeur de type de document de la source, BRUTE.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Le type canonique facture/avoir, ou <see cref="SourceDocumentClassification.Unmapped"/>.</returns>
    Task<SourceDocumentClassification> ClassifyAsync(Guid companyId, string? sourceDocumentKind, CancellationToken cancellationToken = default);
}
