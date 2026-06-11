namespace Liakont.Host.Fiscal;

/// <summary>
/// Saisie éditable du paramétrage fiscal du tenant (FIX301). Toutes les valeurs sont des chaînes liées aux
/// champs du formulaire ; <c>VatOnDebits</c> est un jeton tri-état (<c>"true"</c> / <c>"false"</c> / vide).
/// Une chaîne vide = « non renseigné » = décision de l'expert-comptable en attente = <c>null</c> côté
/// commande = suspension conservée (jamais de valeur par défaut — CLAUDE.md n°2). Mutable (instance
/// partagée avec la page).
/// </summary>
public sealed class FiscalFormModel
{
    /// <summary>TVA sur les débits : jeton <c>"true"</c> (Oui), <c>"false"</c> (Non) ou vide (non renseigné).</summary>
    public string? VatOnDebits { get; set; }

    /// <summary>Catégorie d'opération : nom d'énumération admis (liste fermée) ou vide (non renseigné).</summary>
    public string? OperationCategory { get; set; }

    /// <summary>Méthode d'imputation des frais : nom d'énumération admis (liste fermée) ou vide (non renseigné).</summary>
    public string? FeeImputationMethod { get; set; }

    /// <summary>Fréquence déclarative : chaîne OPAQUE, transmise telle quelle, jamais interprétée (F12-A §3.3).</summary>
    public string? ReportingFrequency { get; set; }
}
