namespace Liakont.Modules.Ingestion.Contracts.Authentication;

/// <summary>
/// Identité d'un agent authentifié par sa clé API : l'agent lui-même et le tenant auquel il
/// appartient (F12 §3.1 — « une clé = un agent = un tenant »). Sert à poser le contexte tenant de
/// la requête (résolution livrée par PIV05, consommée par l'ingestion des documents en PIV04).
/// </summary>
/// <param name="AgentId">Identifiant de l'agent.</param>
/// <param name="TenantId">Slug du tenant propriétaire (route vers sa base de données).</param>
/// <param name="AgentName">Nom de l'agent (diagnostic, journaux opérateur).</param>
public sealed record AgentIdentity(Guid AgentId, string TenantId, string AgentName);
