namespace Liakont.Host.Navigation;

using Liakont.Host.Security;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Détection PARTAGÉE du contexte CROSS-TENANT d'un super-admin (RB1) — source unique pour tous les
/// fournisseurs de navigation (un super-admin <c>stratum-admin</c> n'appartient à aucun tenant ; les
/// surfaces tenant-scopées lui sont masquées). Lit une source SYNCHRONE FIABLE selon le contexte de
/// rendu :
/// <list type="bullet">
/// <item>en CIRCUIT interactif : le drapeau pré-chargé par le circuit handler
/// (<see cref="ILiakontConsoleContext.IsCrossTenantAdmin"/>), le <c>HttpContext</c> étant absent du circuit ;</item>
/// <item>au PRÉRENDU SSR (le circuit handler n'a pas tourné) : le <c>HttpContext.User</c> de la requête.</item>
/// </list>
/// La nav est ainsi correcte DÈS le prérendu (pas de « flash » d'entrées tenant) et même si le circuit
/// ne se connecte jamais (JS désactivé).
/// </summary>
internal static class CrossTenantDetection
{
    public static bool IsCrossTenant(ILiakontConsoleContext console, IHttpContextAccessor httpContextAccessor)
        => console.IsCrossTenantAdmin
           || (httpContextAccessor.HttpContext?.User is { } user && SuperAdminRoles.IsSuperAdmin(user));
}
