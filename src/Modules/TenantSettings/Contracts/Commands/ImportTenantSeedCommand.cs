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

    /// <summary>
    /// Société (companyId) du tenant cible, clé de scoping du paramétrage importé. RENSEIGNÉ pour un
    /// import hors requête opérateur (amorçage de démarrage, endpoint d'administration agissant sur un
    /// tenant donné) : à la création du PREMIER profil, le companyId ne peut être ni lu en base (aucun
    /// profil encore) ni déduit d'un actor HTTP du tenant cible — il vaut le <c>company_id</c> que l'IdP
    /// présentera (claim du realm). <c>null</c> = repli sur le companyId du contexte courant
    /// (<c>ICompanyFilter</c>, chemin requête opérateur). Aucun secret n'est jamais importé
    /// (INV-TENANTSETTINGS-007 inchangé).
    /// </summary>
    public Guid? CompanyId { get; init; }
}
