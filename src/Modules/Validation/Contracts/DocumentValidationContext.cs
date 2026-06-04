namespace Liakont.Modules.Validation.Contracts;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Contexte d'exécution d'une règle de validation : le document pivot reçu et l'identité du tenant
/// auquel il appartient. Une règle qui a besoin de paramétrage tenant (profil émetteur, table TVA…)
/// l'obtient via ses propres dépendances injectées, scopées par <see cref="CompanyId"/> (VAL02+).
/// </summary>
public sealed class DocumentValidationContext
{
    /// <summary>Crée un contexte de validation.</summary>
    /// <param name="document">Le document à valider (modèle pivot EN 16931). Obligatoire.</param>
    /// <param name="companyId">Identité du tenant propriétaire du document (clé d'isolation). Obligatoire.</param>
    public DocumentValidationContext(PivotDocumentDto document, Guid companyId)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Le tenant (companyId) doit être résolu.", nameof(companyId));
        }

        CompanyId = companyId;
    }

    /// <summary>Le document à valider (modèle pivot EN 16931).</summary>
    public PivotDocumentDto Document { get; }

    /// <summary>Identité du tenant propriétaire du document (clé d'isolation multi-tenant, CLAUDE.md n°9).</summary>
    public Guid CompanyId { get; }
}
