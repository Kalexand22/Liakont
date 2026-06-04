namespace Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Nature de l'opération (mention obligatoire de la réforme — F01-F02 §3.1, BT mention FR).
/// Conditionne l'e-reporting de paiement (F09) : une part « prestation de services » déclenche
/// le reporting de paiement sur cette part. La distinction n'est PAS inventée par l'adaptateur ;
/// elle est paramétrée par tenant (cf. F01-F02 §7 décision 3).
/// </summary>
public enum OperationCategory
{
    /// <summary>Livraison de biens.</summary>
    LivraisonBiens = 1,

    /// <summary>Prestation de services.</summary>
    PrestationServices = 2,

    /// <summary>Opération mixte (biens + services), p. ex. adjudication + frais.</summary>
    Mixte = 3,
}
