namespace Liakont.Host.Security;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

/// <summary>
/// Garde d'autorisation des endpoints : lit le claim <see cref="RolePermissionCatalog.PermissionClaimType"/>
/// du principal — le MÊME claim que l'UI (<see cref="ClaimsPermissionService"/>), donc un mécanisme
/// d'autorisation unique (INV-IDN01-3, ADR-0017). Les permissions sont projetées depuis les rôles realm à
/// l'ouverture de session (couche d'auth, abstraction D10) ; aucune requête base par appel. Le court-circuit
/// super-admin (<see cref="SuperAdminRoles"/>) reste inchangé.
/// </summary>
internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (SuperAdminRoles.IsSuperAdmin(context.User))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        foreach (var claim in context.User.FindAll(RolePermissionCatalog.PermissionClaimType))
        {
            if (string.Equals(claim.Value, requirement.Permission, StringComparison.OrdinalIgnoreCase))
            {
                context.Succeed(requirement);
                break;
            }
        }

        return Task.CompletedTask;
    }
}
