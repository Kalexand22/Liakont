namespace Liakont.Modules.TenantSettings.Domain.Entities;

/// <summary>
/// Méthode d'imputation de la part frais (prestation de services) pour l'e-reporting de paiement
/// (F09 §5.2, amendement D2). C'est un PARAMÈTRE de tenant, validé par l'expert-comptable du client :
/// le produit ne tranche JAMAIS à sa place (CLAUDE.md n°2). <c>null</c> au niveau du paramétrage =
/// décision en attente = e-reporting de paiement suspendu — JAMAIS de méthode appliquée par défaut.
/// </summary>
/// <remarks>
/// F09 §5.2 présente deux options NON tranchées par la spec ; le découpage part frais/adjudication
/// d'un document <see cref="OperationCategory.Mixte"/> reste lui-même non sourcé (D-b, ADR-0004 /
/// F03 §2.3) et HORS périmètre de PIP03a (Mixte suspendu). Pour un document mono-catégorie, les deux
/// méthodes donnent le même agrégat jour×taux ; le champ enregistre néanmoins la décision de
/// l'expert-comptable (sa présence — non <c>null</c> — lève la suspension).
/// </remarks>
public enum FeeImputationMethod
{
    /// <summary>Prorata HT frais / HT total du bordereau lié (F09 §5.2, option 1).</summary>
    Prorata = 0,

    /// <summary>Agrégation jour×taux des frais encaissés, sans lettrage ligne à ligne (F09 §5.2, option 2).</summary>
    AgregationJourTaux = 1,
}
