namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Conversion entre les énumérations e-reporting et leurs CODES de fil canoniques DGFiP (TT-81 / TT-15),
/// <b>fail-closed</b> dans les deux sens (CLAUDE.md n°2/3) : un code hors de la liste fermée n'est jamais
/// accepté ni produit. Codes sourcés : G1.68 (catégories de transaction, F03 §2.6) et G7.52 (rôle du
/// déclarant, Annexe 6). Le mapping vers une PA concrète reste dans son plug-in (frontière n°6) ; ce sont
/// ici les codes NORMATIFS, communs à toutes les PA.
/// </summary>
public static class EReportingCodes
{
    /// <summary>Code de fil canonique TT-81 (G1.68) d'une catégorie de transaction.</summary>
    /// <exception cref="ArgumentOutOfRangeException">La valeur n'est pas dans la liste fermée TT-81.</exception>
    public static string ToTransactionCategoryCode(this EReportingTransactionCategory category) => category switch
    {
        EReportingTransactionCategory.Tlb1 => "TLB1",
        EReportingTransactionCategory.Tps1 => "TPS1",
        EReportingTransactionCategory.Tnt1 => "TNT1",
        EReportingTransactionCategory.Tma1 => "TMA1",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Catégorie de transaction e-reporting hors liste fermée TT-81 (G1.68)."),
    };

    /// <summary>
    /// Parse <b>fail-closed</b> d'un code TT-81 (G1.68) : ne reconnaît QUE la liste fermée
    /// {TLB1, TPS1, TNT1, TMA1}. Tout autre code (y compris la coquille « TLS1 ») est rejeté
    /// (<c>false</c>) — jamais deviné (CLAUDE.md n°2/3).
    /// </summary>
    /// <param name="code">Le code candidat (sensible à la casse).</param>
    /// <param name="category">La catégorie reconnue si la méthode renvoie <c>true</c>.</param>
    /// <returns><c>true</c> si le code est dans la liste fermée, sinon <c>false</c>.</returns>
    public static bool TryParseTransactionCategory(string? code, out EReportingTransactionCategory category)
    {
        switch (code)
        {
            case "TLB1": category = EReportingTransactionCategory.Tlb1; return true;
            case "TPS1": category = EReportingTransactionCategory.Tps1; return true;
            case "TNT1": category = EReportingTransactionCategory.Tnt1; return true;
            case "TMA1": category = EReportingTransactionCategory.Tma1; return true;
            default: category = default; return false;
        }
    }

    /// <summary>Code de fil canonique TT-15 (G7.52) d'un rôle de déclarant.</summary>
    /// <exception cref="ArgumentOutOfRangeException">La valeur n'est pas dans la liste fermée TT-15.</exception>
    public static string ToDeclarantRoleCode(this EReportingDeclarantRole role) => role switch
    {
        EReportingDeclarantRole.Buyer => "BY",
        EReportingDeclarantRole.Seller => "SE",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Rôle de déclarant e-reporting hors liste fermée TT-15 (G7.52)."),
    };

    /// <summary>Parse <b>fail-closed</b> d'un code rôle TT-15 (G7.52) : ne reconnaît que {BY, SE}.</summary>
    /// <param name="code">Le code candidat (sensible à la casse).</param>
    /// <param name="role">Le rôle reconnu si la méthode renvoie <c>true</c>.</param>
    /// <returns><c>true</c> si le code est dans la liste fermée, sinon <c>false</c>.</returns>
    public static bool TryParseDeclarantRole(string? code, out EReportingDeclarantRole role)
    {
        switch (code)
        {
            case "BY": role = EReportingDeclarantRole.Buyer; return true;
            case "SE": role = EReportingDeclarantRole.Seller; return true;
            default: role = default; return false;
        }
    }
}
