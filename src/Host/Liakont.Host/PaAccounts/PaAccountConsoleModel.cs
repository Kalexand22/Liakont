namespace Liakont.Host.PaAccounts;

using System.Collections.Generic;
using Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Données assemblées de la page « Comptes plateforme agréée » (FIX01c) : les comptes PA du tenant
/// courant (sans jamais la clé — seul <see cref="PaAccountDto.HasApiKey"/>) et les types de plug-ins
/// actuellement enregistrés par le Host, proposés à la création (CLAUDE.md n°16 : aucune liste de PA
/// concrets en dur — toujours depuis le registre <c>IPaClientRegistry</c>).
/// </summary>
public sealed class PaAccountConsoleModel
{
    /// <summary>Comptes PA du tenant courant (lecture seule des métadonnées ; jamais la clé).</summary>
    public required IReadOnlyList<PaAccountDto> Accounts { get; init; }

    /// <summary>Types de plug-ins PA enregistrés (clé du registre), triés, proposés en création.</summary>
    public required IReadOnlyList<string> RegisteredPluginTypes { get; init; }
}
