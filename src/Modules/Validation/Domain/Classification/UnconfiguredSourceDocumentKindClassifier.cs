namespace Liakont.Modules.Validation.Domain.Classification;

using Liakont.Modules.Validation.Contracts.Classification;

/// <summary>
/// Classificateur par DÉFAUT : répond <see cref="SourceDocumentClassification.Unmapped"/> pour toute
/// valeur de type source, tant qu'AUCUNE table de correspondance tenant n'est provisionnée (F04 §3.5bis).
/// État honnête « aucune correspondance configurée », NON une invention : la nature avoir reste alors
/// détectée par son signal STRUCTUREL (référence d'origine EN 16931 BG-3, <c>CreditNoteRule</c>) — repli
/// rétro-compatible.
/// </summary>
/// <remarks>
/// Enregistré comme implémentation par défaut de <see cref="ISourceDocumentKindClassifier"/> par
/// <c>ValidationModuleRegistration</c>. L'item de suivi (persistance de la table tenant + import seed,
/// voir F04 §3.5bis) substituera une implémentation adossée à la table par <c>services.Replace</c>
/// (précédent SUP03 <c>StubEmailTransport</c> → <c>SmtpEmailTransport</c>). Le séparer du défaut isole
/// l'unique arête qui « fait quelque chose » (la table) du mécanisme de validation, déjà câblé et testé.
/// </remarks>
public sealed class UnconfiguredSourceDocumentKindClassifier : ISourceDocumentKindClassifier
{
    /// <inheritdoc />
    public Task<SourceDocumentClassification> ClassifyAsync(Guid companyId, string? sourceDocumentKind, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SourceDocumentClassification.Unmapped);
    }
}
