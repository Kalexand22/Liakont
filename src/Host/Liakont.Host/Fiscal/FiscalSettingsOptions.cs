namespace Liakont.Host.Fiscal;

using System.Collections.Generic;

/// <summary>
/// Listes fermées des valeurs admises par le contrat fiscal (<c>SetFiscalSettingsCommand</c>) : catégorie
/// d'opération (énumération <c>OperationCategory</c>, F12-A §3.2 / F09 §1) et méthode d'imputation des frais
/// (énumération <c>FeeImputationMethod</c>, F09 §5.2). Chaque <see cref="FiscalOption.Value"/> est le NOM
/// d'énumération exact du contrat — aucune valeur inventée (CLAUDE.md n°2) ; le libellé n'est qu'une
/// présentation française. La coïncidence stricte de ces listes avec les énumérations du contrat (aucune
/// valeur en trop, aucune manquante, même ordre) est GARANTIE par <c>FiscalConsoleServiceTests</c> (qui
/// référence les énumérations du domaine) : toute dérive devient un test rouge.
/// </summary>
/// <remarks>
/// Le contrat est volontairement à base de <c>string</c> ; le Host le consomme comme tel, sans coupler la
/// présentation au domaine TenantSettings. La traçabilité « pas de liste inventée » est portée par le test,
/// pas par une référence au type du domaine.
/// </remarks>
public static class FiscalSettingsOptions
{
    /// <summary>Catégories d'opération admises (liste fermée = énumération <c>OperationCategory</c> du contrat).</summary>
    public static IReadOnlyList<FiscalOption> OperationCategories { get; } =
    [
        new FiscalOption("LivraisonBiens", "Livraison de biens"),
        new FiscalOption("PrestationServices", "Prestation de services"),
        new FiscalOption("Mixte", "Mixte (biens + frais de service)"),
    ];

    /// <summary>Méthodes d'imputation des frais admises (liste fermée = énumération <c>FeeImputationMethod</c> du contrat).</summary>
    public static IReadOnlyList<FiscalOption> FeeImputationMethods { get; } =
    [
        new FiscalOption("Prorata", "Prorata (HT frais / HT total)"),
        new FiscalOption("AgregationJourTaux", "Agrégation jour × taux"),
    ];
}
