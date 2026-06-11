namespace Liakont.Host.Fiscal;

using System.Collections.Generic;

/// <summary>
/// Modèle assemblé de l'écran « Paramétrage › Fiscal » (FIX301) : la saisie éditable pré-remplie aux valeurs
/// actuelles du tenant (ou « non renseigné »), et les listes fermées admises par le contrat. Aucune valeur par
/// défaut n'est appliquée : un champ vide signifie « décision en attente » (suspension conservée).
/// </summary>
public sealed record FiscalViewModel
{
    /// <summary>Valeurs éditables, pré-remplies au paramétrage fiscal actuel du tenant.</summary>
    public required FiscalFormModel Form { get; init; }

    /// <summary>Catégories d'opération admises (liste fermée — source : énumération du contrat).</summary>
    public required IReadOnlyList<FiscalOption> OperationCategoryOptions { get; init; }

    /// <summary>Méthodes d'imputation des frais admises (liste fermée — source : énumération du contrat).</summary>
    public required IReadOnlyList<FiscalOption> FeeImputationMethodOptions { get; init; }
}
