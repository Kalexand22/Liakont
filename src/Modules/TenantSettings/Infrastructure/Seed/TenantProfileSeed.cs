namespace Liakont.Modules.TenantSettings.Infrastructure.Seed;

/// <summary>
/// Modèle de désérialisation de <c>tenant-profile.json</c> (F12-A §8.1). Ne porte QUE du PARAMÉTRAGE
/// réutilisable (fiscal / planification / seuils) — l'identité légale (SIREN, raison sociale, adresse,
/// contact) n'est JAMAIS seedée (BUG-14 : donnée tenant-spécifique réelle, saisie manuellement à la
/// création, jamais écrasée par une baseline de démo). Permissif (champs nullables) : la validation
/// métier est faite par le domaine à l'application. La clé API n'est pas modélisée : l'import n'écrit
/// jamais un secret (F12-A §8.2).
/// </summary>
internal sealed record TenantProfileSeed
{
    public FiscalSeed? Fiscal { get; init; }

    public ScheduleSeed? Schedule { get; init; }

    public ThresholdsSeed? Thresholds { get; init; }
}
