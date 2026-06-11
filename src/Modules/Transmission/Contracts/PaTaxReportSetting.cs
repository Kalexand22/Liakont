namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Réglage de tax report d'un compte PA, tel que LU auprès de la PA (F05 §2, B.3). DTO neutre : il
/// transporte des valeurs de paramétrage (jamais inventées par le code — elles viennent du tenant,
/// CFG02 ; CLAUDE.md n°2/7). Champ absent = <c>null</c>.
/// </summary>
public sealed record PaTaxReportSetting
{
    /// <summary>Code NAF/INSEE déclaré, ou <c>null</c> si non réglé.</summary>
    public string? NafCode { get; init; }

    /// <summary>
    /// Date de début de publication (F05 §2 : si future → SIREN non publié, aucun envoi possible),
    /// ou <c>null</c> si non réglée.
    /// </summary>
    public DateOnly? StartDate { get; init; }

    /// <summary>Type d'opération déclaré côté PA, ou <c>null</c> si non réglé.</summary>
    public string? TypeOperation { get; init; }

    /// <summary>Taille d'entreprise déclarée, ou <c>null</c> si non réglée.</summary>
    public string? EnterpriseSize { get; init; }

    /// <summary>Schéma d'identification du compte (F05 §2 : « 0002 » = SIREN), ou <c>null</c>.</summary>
    public string? CinScheme { get; init; }

    /// <summary>Réponse brute de la PA, conservée pour l'audit (peut être <c>null</c>).</summary>
    public string? RawResponse { get; init; }

    /// <summary>
    /// Le SIREN est PUBLIÉ / la transmission est ACTIVE à la date <paramref name="today"/> : la date de
    /// début est renseignée ET non future (F05 §2 : une <c>start_date</c> future = SIREN non publié, aucun
    /// envoi possible). SOURCE UNIQUE de cette règle d'activation — consommée par le gating d'envoi
    /// (diagnostic pré-envoi F04 §3.1) ET par l'affichage de l'état de publication dans la console, pour
    /// qu'ils ne puissent jamais diverger.
    /// </summary>
    /// <param name="today">Jour de référence (UTC) à comparer à la date de début.</param>
    public bool IsActiveOn(DateOnly today) => StartDate is { } startDate && startDate <= today;
}
