namespace Liakont.Agent.Contracts.Transport;

/// <summary>
/// Régime de TVA observé dans le système source (F12 ; IExtractor.ListSourceTaxRegimes). Transmis
/// par l'agent comme métadonnée de push pour alimenter le paramétrage de la table de mapping (F03)
/// et détecter les régimes non couverts (TVA03). Valeur BRUTE : l'agent n'interprète jamais le
/// régime (CLAUDE.md n°2).
/// </summary>
public sealed class SourceTaxRegimeDto
{
    /// <summary>Crée un régime de TVA source.</summary>
    /// <param name="code">Code du régime dans le système source (brut). Obligatoire.</param>
    /// <param name="label">Libellé du régime dans le système source, si présent.</param>
    /// <param name="occurrences">Nombre d'occurrences observées sur la période (0 si non compté).</param>
    public SourceTaxRegimeDto(string code, string? label = null, int occurrences = 0)
    {
        Code = code;
        Label = label;
        Occurrences = occurrences;
    }

    /// <summary>Code du régime dans le système source (brut).</summary>
    public string Code { get; }

    /// <summary>Libellé du régime dans le système source.</summary>
    public string? Label { get; }

    /// <summary>Nombre d'occurrences observées sur la période.</summary>
    public int Occurrences { get; }
}
