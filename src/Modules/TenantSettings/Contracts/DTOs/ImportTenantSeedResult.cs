namespace Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Résultat d'un import de seed <c>deployments/&lt;client&gt;/</c> (F12-A §8). Idempotent
/// (crée ou met à jour). <see cref="Warnings"/> trace notamment les clés API laissées vides
/// (placeholders) à compléter via la console — jamais de secret écrit en clair.
/// </summary>
public record ImportTenantSeedResult
{
    public required bool ProfileImported { get; init; }

    public required bool FiscalImported { get; init; }

    public required int PaAccountsImported { get; init; }

    public required bool ScheduleImported { get; init; }

    public required bool ThresholdsImported { get; init; }

    /// <summary>
    /// <c>true</c> si une table de mapping TVA a été importée depuis le seed (item FIX01b) ; <c>false</c>
    /// si aucun fichier de mapping n'était présent OU si une table existait déjà (import idempotent ignoré).
    /// </summary>
    public bool TvaMappingImported { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}
