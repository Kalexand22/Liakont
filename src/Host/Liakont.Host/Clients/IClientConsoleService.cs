namespace Liakont.Host.Clients;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Security.Abstractions;

/// <summary>
/// Service console de l'écran « Clients » (OPS03) : liste des tenants de l'instance (registre système +
/// profil par scope tenant + compteur d'agents), création de tenant, seed/profil, premier utilisateur,
/// premier agent, suspension/réactivation. Tout dispatch est IN-PROCESS (pattern WEB09 — jamais de HTTP
/// en boucle locale) ; la garde d'accès est la page (<c>liakont.supervision</c>, parité WEB09 documentée).
/// </summary>
internal interface IClientConsoleService
{
    /// <summary>Tous les tenants de l'instance — un tenant illisible reste VISIBLE (ReadFailed).</summary>
    Task<IReadOnlyList<ClientConsoleLine>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Noms des dossiers de seed disponibles sous la racine configurée (TenantSeeds:RootPath).</summary>
    IReadOnlyList<string> ListSeedDirectories();

    /// <summary>Crée le tenant (base + realm + registre) — idempotent (AlreadyProvisioned = reprise, pas une erreur).</summary>
    Task<ClientCreationResult> CreateTenantAsync(
        string tenantId, string displayName, string adminEmail, CancellationToken cancellationToken = default);

    /// <summary>Importe le seed (dossier SOUS la racine configurée) dans le tenant cible — companyId du registre.</summary>
    Task<ClientSeedResult> ImportSeedAsync(string tenantId, string seedDirectoryName, CancellationToken cancellationToken = default);

    /// <summary>Crée le profil saisi manuellement (chemin « sans seed ») dans le tenant cible.</summary>
    Task<ClientActionResult> SaveProfileAsync(string tenantId, ClientProfileInput profile, CancellationToken cancellationToken = default);

    /// <summary>Provisionne le premier utilisateur (compte IdP + applicatif + invitation) — délègue au lot A.</summary>
    Task<TenantUserProvisionResult> ProvisionFirstUserAsync(
        TenantUserProvisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Enregistre le premier agent du tenant cible (PIV05) — clé complète remise UNE fois.</summary>
    Task<ClientAgentKeyResult> RegisterFirstAgentAsync(string tenantId, string agentName, CancellationToken cancellationToken = default);

    /// <summary>Suspend ou réactive le tenant (statut métier) — effet immédiat (cache de suspension invalidé).</summary>
    Task<ClientActionResult> SetStatusAsync(string tenantId, bool suspendre, CancellationToken cancellationToken = default);
}
