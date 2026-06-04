namespace Liakont.Modules.TenantSettings.Infrastructure.Seed;

/// <summary>
/// Modèle de désérialisation de <c>tenant-profile.json</c> (F12-A §8.1). Permissif (champs
/// nullables) : la validation métier est faite par le domaine à l'application. La clé API n'est
/// pas modélisée : l'import n'écrit jamais un secret (F12-A §8.2).
/// </summary>
internal sealed record TenantProfileSeed
{
    public string? Siren { get; init; }

    public string? RaisonSociale { get; init; }

    public AddressSeed? Address { get; init; }

    public string? ContactEmailAlerte { get; init; }

    public FiscalSeed? Fiscal { get; init; }

    public ScheduleSeed? Schedule { get; init; }

    public ThresholdsSeed? Thresholds { get; init; }
}
