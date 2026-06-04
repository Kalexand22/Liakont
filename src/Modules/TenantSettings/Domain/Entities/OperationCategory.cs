namespace Liakont.Modules.TenantSettings.Domain.Entities;

/// <summary>
/// Nature de l'opération du flux (F12-A §3.2, valeurs issues de F01-F02 / F09 §1).
/// <c>null</c> au niveau du paramétrage = décision de l'expert-comptable en attente
/// = transmissions dépendantes suspendues (jamais de valeur devinée — CLAUDE.md n°2).
/// Ne JAMAIS ajouter de valeur hors de cette énumération sourcée.
/// </summary>
public enum OperationCategory
{
    /// <summary>Livraison de biens.</summary>
    LivraisonBiens = 0,

    /// <summary>Prestation de services.</summary>
    PrestationServices = 1,

    /// <summary>Mixte (ex. adjudication = biens + frais de service) — F09 §1.</summary>
    Mixte = 2,
}
