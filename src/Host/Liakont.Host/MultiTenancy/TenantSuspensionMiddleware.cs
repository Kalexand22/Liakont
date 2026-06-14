namespace Liakont.Host.MultiTenancy;

using System;
using System.Threading.Tasks;
using Liakont.Host.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Applique le statut SUSPENDU d'un tenant aux UTILISATEURS (OPS03.4 lot B) : sessions console déjà
/// ouvertes ET API Bearer — le refus au sign-in OIDC est porté par la couche d'auth
/// (<c>OnTokenValidated</c>). Inséré APRÈS <c>UseStratumMultiTenancy</c> (tenant résolu, principal
/// authentifié) et AVANT <c>UseAuthorization</c>. Jamais appliqué : aux requêtes anonymes (login,
/// page de suspension, API agent — couverte par <see cref="Liakont.Host.AgentApi.TenantSuspendedPushFilter"/>),
/// ni aux super-admins (l'opérateur d'instance doit pouvoir RÉACTIVER — <c>/admin/*</c> reste servi).
/// Les données du tenant restent intactes : refus à la frontière, jamais une purge. Fenêtre
/// résiduelle assumée : un circuit Blazor déjà ouvert n'est coupé qu'au prochain aller-retour HTTP
/// (cohérent avec la fenêtre de révocation acceptée d'ADR-0017).
/// </summary>
internal sealed class TenantSuspensionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantSuspensionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        ITenantSuspensionLookup suspensionLookup)
    {
        if (context.User.Identity?.IsAuthenticated != true
            || SuperAdminRoles.IsSuperAdmin(context.User)
            || string.IsNullOrEmpty(tenantContext.TenantId)
            || !await suspensionLookup.IsSuspendedAsync(tenantContext.TenantId, context.RequestAborted))
        {
            await _next(context);
            return;
        }

        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            // API (Bearer/cookie) : 403 explicite en français — jamais une 500.
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(
                "{\"message\":\"Tenant suspendu — l'accès à la console est désactivé. Contactez votre opérateur Liakont.\"}",
                context.RequestAborted);
            return;
        }

        // UI : la session est fermée et l'utilisateur arrive sur la page explicite (anonyme — pas de
        // boucle de redirection). Le cookie est posé par le schéma Cookies du flux OIDC.
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        context.Response.Redirect("/tenant-suspendu");
    }
}
