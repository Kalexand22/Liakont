namespace Liakont.Host.AgentApi;

using Liakont.Modules.Ingestion.Contracts.Authentication;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Transporte l'identité de l'agent authentifié depuis le filtre d'authentification (<see
/// cref="AgentApiAuthenticationFilter"/>) vers les endpoints de l'API agent, via
/// <see cref="HttpContext.Items"/>.
/// </summary>
internal static class AgentApiContext
{
    private const string IdentityKey = "Liakont.AgentIdentity";

    public static void SetIdentity(HttpContext http, AgentIdentity identity) =>
        http.Items[IdentityKey] = identity;

    public static AgentIdentity GetIdentity(HttpContext http)
    {
        if (http.Items.TryGetValue(IdentityKey, out var value) && value is AgentIdentity identity)
        {
            return identity;
        }

        throw new InvalidOperationException(
            "Identité d'agent absente du contexte : le filtre d'authentification agent n'a pas été appliqué.");
    }
}
