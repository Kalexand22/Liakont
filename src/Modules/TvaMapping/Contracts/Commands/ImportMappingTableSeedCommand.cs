namespace Liakont.Modules.TvaMapping.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Importe la table de mapping TVA d'un fichier de seed (item FIX01b, format
/// <c>config/exemples/mapping-exemple.json</c> / <c>deployments/&lt;client&gt;/</c>) dans le tenant
/// courant — chemin câblé sur le point d'entrée OPS03 (import de seed tenant). IDEMPOTENT : si une table
/// est déjà paramétrée pour ce tenant, l'import est ignoré (jamais d'écrasement d'une table éditée par
/// l'opérateur). Le marqueur « table d'exemple » et l'état « NON VALIDÉE » du seed sont conservés tels
/// quels (le garde-fou d'envoi en production reste actif). Aucune règle fiscale devinée (CLAUDE.md n°2) :
/// tout code inconnu du seed est rejeté. Tenant résolu par le contexte (CLAUDE.md n°9).
/// </summary>
/// <returns><c>true</c> si la table a été importée ; <c>false</c> si une table existait déjà (ignoré).</returns>
public sealed record ImportMappingTableSeedCommand : ICommand<bool>
{
    /// <summary>Chemin du fichier de seed de mapping TVA à importer (JSON).</summary>
    public required string SeedFilePath { get; init; }

    /// <summary>
    /// Société (companyId) du tenant cible, clé de scoping de la table importée. RENSEIGNÉ pour un import
    /// hors requête opérateur (amorçage de démarrage, provisioning OPS03 agissant sur un tenant donné) :
    /// au démarrage AUCUN contexte de société ambiant n'est posé (pas d'acteur HTTP de tenant), donc
    /// <c>ICompanyFilter.GetRequiredCompanyId()</c> échouerait — c'est précisément le seed partiel
    /// corrigé par FIX203a. <c>null</c> = repli sur le companyId du contexte courant
    /// (<c>ICompanyFilter</c>, chemin requête opérateur). Aligné sur <c>ImportTenantSeedCommand.CompanyId</c>
    /// (FIX01a) pour que l'import de seed propage la MÊME clé de scoping à ses deux côtés (paramétrage + table).
    /// <para>
    /// Garde anti-injection cross-tenant : un override qui CONTREDIT la société d'un acteur de tenant
    /// présent est REFUSÉ par le handler (la valeur n'est honorée que sur les chemins de provisioning
    /// sans acteur de tenant — amorçage, endpoint d'administration). Ne JAMAIS lier <see cref="CompanyId"/>
    /// depuis le corps d'une requête opérateur.
    /// </para>
    /// </summary>
    public Guid? CompanyId { get; init; }
}
