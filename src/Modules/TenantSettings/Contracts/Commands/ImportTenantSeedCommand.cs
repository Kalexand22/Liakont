namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Importe le seed de paramétrage d'un dossier <c>deployments/&lt;client&gt;/</c> dans le tenant
/// courant (F12-A §8, consommé par le provisioning OPS03). Idempotent (crée ou met à jour).
/// N'écrit JAMAIS un secret en clair : les placeholders de clé API restent vides (à compléter via
/// la console). Cible le tenant du contexte courant (tenant-scoping, CLAUDE.md n°9).
/// </summary>
public record ImportTenantSeedCommand : ICommand<ImportTenantSeedResult>
{
    /// <summary>Chemin du dossier de seed (ex. <c>deployments/cmp/</c>).</summary>
    public required string SeedDirectoryPath { get; init; }
}
