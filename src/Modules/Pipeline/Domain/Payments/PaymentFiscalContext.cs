namespace Liakont.Modules.Pipeline.Domain.Payments;

/// <summary>
/// Contexte fiscal du tenant pour l'agrégation de paiement (F09, F12-A §3), exprimé en types neutres
/// (le module Pipeline ne référence pas le Domain de TenantSettings — frontière P1 ; les valeurs viennent
/// de <c>FiscalSettingsDto</c> en Contracts). Tout paramètre manquant = décision de l'expert-comptable en
/// attente = suspension (CLAUDE.md n°2). La VALEUR de la méthode d'imputation n'est pas portée ici : pour un
/// document mono-catégorie les deux options F09 §5.2 donnent le même agrégat — seule sa PRÉSENCE compte.
/// </summary>
public sealed record PaymentFiscalContext
{
    /// <summary>Option TVA sur les débits. <c>true</c> = pas d'e-reporting de paiement (non requis) ; <c>null</c> = suspension.</summary>
    public bool? VatOnDebits { get; init; }

    /// <summary>La catégorie d'opération du tenant est renseignée (F12-A §9 #4). <c>false</c> = suspension.</summary>
    public bool HasOperationCategory { get; init; }

    /// <summary>La fréquence déclarative est renseignée (F12-A §3.3). <c>false</c> = suspension.</summary>
    public bool HasReportingFrequency { get; init; }

    /// <summary>La méthode d'imputation des frais est renseignée (F09 §5.2). <c>false</c> = suspension (jamais de prorata par défaut).</summary>
    public bool HasFeeImputationMethod { get; init; }
}
