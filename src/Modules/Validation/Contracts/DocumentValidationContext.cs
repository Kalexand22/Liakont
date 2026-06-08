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
    /// <param name="buyerConfirmedAsIndividual">
    /// L'opérateur a tranché que l'acheteur est un PARTICULIER (B2C) malgré l'indice professionnel — verdict
    /// du garde-fou B2B/B2C (F08 §A.4 : « confirmer B2C → débloque en B2C, décision journalisée »). Décision
    /// SANCTIONNÉE par la spec et tracée dans la piste d'audit (item API02b) ; elle constitue une ENTRÉE
    /// légitime de la validation, jamais un affaiblissement silencieux d'une anomalie bloquante (CLAUDE.md n°3).
    /// Par défaut <c>false</c> (le cas nominal : aucun verdict opérateur).
    /// </param>
    public DocumentValidationContext(PivotDocumentDto document, Guid companyId, bool buyerConfirmedAsIndividual = false)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Le tenant (companyId) doit être résolu.", nameof(companyId));
        }

        CompanyId = companyId;
        BuyerConfirmedAsIndividual = buyerConfirmedAsIndividual;
    }

    /// <summary>Le document à valider (modèle pivot EN 16931).</summary>
    public PivotDocumentDto Document { get; }

    /// <summary>Identité du tenant propriétaire du document (clé d'isolation multi-tenant, CLAUDE.md n°9).</summary>
    public Guid CompanyId { get; }

    /// <summary>
    /// Verdict opérateur « acheteur confirmé particulier (B2C) » du garde-fou B2B/B2C (F08 §A.4) : quand il est
    /// <c>true</c>, <see cref="Liakont.Modules.Validation.Domain.Rules.BuyerLooksProfessionalRule"/> ne produit
    /// PAS l'anomalie <c>BUYER_LOOKS_PROFESSIONAL</c> pour ce document (la décision de l'opérateur, journalisée,
    /// prime sur l'heuristique d'indice). La règle reste détection-seule pour tout document non tranché.
    /// </summary>
    public bool BuyerConfirmedAsIndividual { get; }
}
