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
}
