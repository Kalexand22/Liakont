namespace Liakont.Modules.TenantSettings.Infrastructure;

using Liakont.Modules.TenantSettings.Domain.Entities;

/// <summary>
/// Conversion des valeurs d'énumération reçues en chaîne (Contracts) vers les types du domaine.
/// Toute valeur inconnue est REJETÉE — jamais de valeur devinée (CLAUDE.md n°2).
/// </summary>
internal static class TenantSettingsParsing
{
    public static TenantStatus ParseStatus(string value)
    {
        if (Enum.TryParse<TenantStatus>(value?.Trim(), ignoreCase: true, out var status) && Enum.IsDefined(status))
        {
            return status;
        }

        throw new ArgumentException(
            $"Statut de tenant inconnu : « {value} » (attendu : Actif | Suspendu).",
            nameof(value));
    }

    public static PaEnvironment ParseEnvironment(string value)
    {
        if (Enum.TryParse<PaEnvironment>(value?.Trim(), ignoreCase: true, out var environment) && Enum.IsDefined(environment))
        {
            return environment;
        }

        throw new ArgumentException(
            $"Environnement PA inconnu : « {value} » (attendu : Staging | Production).",
            nameof(value));
    }

    /// <summary>Convertit la catégorie d'opération. <c>null</c>/vide = décision en attente (suspension), jamais devinée.</summary>
    public static OperationCategory? ParseOperationCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse<OperationCategory>(value.Trim(), ignoreCase: true, out var category) && Enum.IsDefined(category))
        {
            return category;
        }

        throw new ArgumentException(
            $"Catégorie d'opération inconnue : « {value} » (attendu : LivraisonBiens | PrestationServices | Mixte).",
            nameof(value));
    }

    /// <summary>Convertit la méthode d'imputation des frais. <c>null</c>/vide = décision en attente (suspension), jamais devinée.</summary>
    public static FeeImputationMethod? ParseFeeImputationMethod(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse<FeeImputationMethod>(value.Trim(), ignoreCase: true, out var method) && Enum.IsDefined(method))
        {
            return method;
        }

        throw new ArgumentException(
            $"Méthode d'imputation des frais inconnue : « {value} » (attendu : Prorata | AgregationJourTaux).",
            nameof(value));
    }
}
