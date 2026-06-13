namespace Liakont.Host.PaAccounts;

using System.Collections.Generic;
using Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Données assemblées de la page « Comptes plateforme agréée » (FIX01c) : les comptes PA du tenant
/// courant (sans jamais la clé — seul <see cref="PaAccountDto.HasApiKey"/>), AVEC leurs capacités
/// déclarées (lot polish UX/UI : le détail des capacités vit ici, plus sur le hub Paramétrage), et
/// les types de plug-ins actuellement enregistrés par le Host, proposés à la création (CLAUDE.md
/// n°16 : aucune liste de PA concrets en dur — toujours depuis le registre <c>IPaClientRegistry</c>).
/// </summary>
public sealed class PaAccountConsoleModel
{
    /// <summary>
    /// Comptes PA du tenant courant avec capacités résolues (lecture seule des métadonnées ; jamais
    /// la clé — <see cref="PaAccountSettingsDto.Capabilities"/> est <c>null</c> si le plug-in n'est
    /// pas chargé).
    /// </summary>
    public required IReadOnlyList<PaAccountSettingsDto> Accounts { get; init; }

    /// <summary>Types de plug-ins PA enregistrés (clé du registre), triés, proposés en création.</summary>
    public required IReadOnlyList<string> RegisteredPluginTypes { get; init; }
}
