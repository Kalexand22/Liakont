namespace Liakont.Host.Fiscal;

/// <summary>
/// Valeurs brutes du formulaire fiscal transmises au service à l'enregistrement (FIX301). Le service convertit
/// le jeton tri-état <see cref="VatOnDebits"/> en <c>bool?</c> et normalise les chaînes vides en <c>null</c>
/// avant d'émettre <c>SetFiscalSettingsCommand</c> ; la validation des valeurs (listes fermées, rejet d'une
/// valeur inconnue) reste du ressort du handler (CLAUDE.md n°2/3).
/// </summary>
public sealed record FiscalSettingsInput
{
    /// <summary>Jeton tri-état TVA sur les débits : <c>"true"</c> / <c>"false"</c> / vide (non renseigné).</summary>
    public string? VatOnDebits { get; init; }

    /// <summary>Catégorie d'opération (nom d'énumération admis) ou vide.</summary>
    public string? OperationCategory { get; init; }

    /// <summary>Méthode d'imputation des frais (nom d'énumération admis) ou vide.</summary>
    public string? FeeImputationMethod { get; init; }

    /// <summary>Fréquence déclarative opaque ou vide.</summary>
    public string? ReportingFrequency { get; init; }
}
