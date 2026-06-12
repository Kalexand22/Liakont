namespace Liakont.Host.AgentApi;

using System.Threading.Tasks;
using Liakont.Host.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Refuse le PUSH d'un tenant suspendu (OPS03.4 lot B) : appliqué aux SEULS endpoints d'ÉCRITURE
/// de l'API agent (batch, PDF lié, pool PDF), APRÈS <see cref="AgentApiAuthenticationFilter"/>
/// (l'identité — donc le tenant — est déjà résolue). Les endpoints de lecture/diagnostic
/// (heartbeat, configuration, statuts) restent servis : l'agent demeure supervisé, ne déclenche
/// pas le diagnostic trompeur « clé révoquée », et reprend SEUL après réactivation.
/// Côté agent, le 403 arrête immédiatement le drainage SANS PERTE (les documents restent dans la
/// file locale — comportement contractuel existant, aucun changement agent). Les données déjà
/// transmises restent intactes : la suspension est un refus à la frontière, jamais une purge.
/// </summary>
internal sealed class TenantSuspendedPushFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var identity = AgentApiContext.GetIdentity(context.HttpContext);
        var lookup = context.HttpContext.RequestServices.GetRequiredService<ITenantSuspensionLookup>();

        if (await lookup.IsSuspendedAsync(identity.TenantId, context.HttpContext.RequestAborted))
        {
            // Message opérateur français (CLAUDE.md n°12), distinct d'une clé révoquée pour qui lit
            // les journaux de l'agent. 403 = l'agent arrête le drainage sans perte et réessaiera.
            return Results.Json(
                new
                {
                    message = "Tenant suspendu par l'opérateur : les documents ne sont plus acceptés. "
                        + "Les données déjà transmises sont conservées et l'envoi reprendra à la réactivation. "
                        + "Contactez votre opérateur Liakont.",
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}
