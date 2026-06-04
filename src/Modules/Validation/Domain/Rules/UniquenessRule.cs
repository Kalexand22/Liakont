namespace Liakont.Modules.Validation.Domain.Rules;

using Liakont.Modules.Validation.Contracts;

/// <summary>
/// Anti-doublon de numéro de document (F04 §3.3) : le numéro doit être présent ET ne pas avoir déjà
/// été émis pour le tenant. La recherche d'unicité est déléguée au module Documents via le port
/// <see cref="IIssuedDocumentLookup"/> (implémentation réelle : lot TRK, item TRK03 ; un faux d'essai
/// suffit ici). Requête TENANT-SCOPÉE (CLAUDE.md n°9). Les deux contrôles sont BLOQUANTS : un numéro
/// absent ou réémis garantit un rejet PA ou un doublon de transmission.
/// </summary>
public sealed class UniquenessRule : IDocumentRule
{
    /// <summary>Numéro de document absent.</summary>
    public const string NumberMissingCode = "DOC_NUMBER_MISSING";

    /// <summary>Numéro de document déjà émis pour ce tenant.</summary>
    public const string DuplicateCode = "DOC_NUMBER_DUPLICATE";

    private readonly IIssuedDocumentLookup _issuedDocumentLookup;

    /// <summary>Crée la règle d'unicité.</summary>
    /// <param name="issuedDocumentLookup">Port d'interrogation des documents déjà émis du tenant.</param>
    public UniquenessRule(IIssuedDocumentLookup issuedDocumentLookup)
    {
        ArgumentNullException.ThrowIfNull(issuedDocumentLookup);
        _issuedDocumentLookup = issuedDocumentLookup;
    }

    /// <inheritdoc />
    public string Code => "UNIQUENESS";

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var document = context.Document;

        // Numéro absent : on bloque et on n'interroge pas le module Documents (rien à rechercher).
        if (string.IsNullOrWhiteSpace(document.Number))
        {
            return new[]
            {
                ValidationIssue.Blocking(
                    NumberMissingCode,
                    "Le document est dépourvu de numéro. Un numéro de document est obligatoire pour la transmission.",
                    "Document.Number est vide.",
                    "BT-1"),
            };
        }

        var alreadyIssued = await _issuedDocumentLookup.IsAlreadyIssuedAsync(context.CompanyId, document.Number, cancellationToken);
        if (!alreadyIssued)
        {
            return Array.Empty<ValidationIssue>();
        }

        return new[]
        {
            ValidationIssue.Blocking(
                DuplicateCode,
                $"Le numéro de document {document.Number} a déjà été émis pour ce client. Un même numéro ne peut pas être transmis deux fois.",
                $"IIssuedDocumentLookup.IsAlreadyIssuedAsync(companyId, '{document.Number}') = true.",
                "BT-1"),
        };
    }
}
