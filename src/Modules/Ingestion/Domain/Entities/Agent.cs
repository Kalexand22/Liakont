namespace Liakont.Modules.Ingestion.Domain.Entities;

using Stratum.Common.Abstractions.Exceptions;

/// <summary>
/// Agent d'extraction enregistré pour un tenant (F12 §3.2, §4.2). Un tenant peut détenir plusieurs
/// agents (rare : multi-sites), chacun avec sa propre clé API. L'entité vit dans le REGISTRE
/// SYSTÈME (base partagée) : c'est ce qui permet de résoudre une clé API vers son tenant AVANT
/// d'ouvrir la base du tenant (l'authentification précède tout contexte tenant). Chaque ligne porte
/// son <see cref="TenantId"/> (slug, jamais cross-tenant à l'usage).
/// </summary>
/// <remarks>
/// <strong>Sécurité :</strong> la clé API n'est JAMAIS stockée ni renvoyée en clair. Seuls le
/// <see cref="KeyPrefix"/> (public, indexé) et l'empreinte SHA-256 (<see cref="KeyHash"/>) sont
/// persistés. La clé complète n'existe qu'à la génération (<see cref="Create"/>/<see cref="RotateKey"/>),
/// affichée une seule fois (F12 §4.2, CLAUDE.md n°10).
/// </remarks>
public sealed class Agent
{
    private Agent()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Tenant propriétaire (slug d'identification tenant — route vers sa base de données).</summary>
    public string TenantId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>Identifiant public de la clé (partie gauche de <c>prefix.secret</c>), indexé pour la résolution.</summary>
    public string KeyPrefix { get; private set; } = string.Empty;

    /// <summary>Empreinte SHA-256 (hex) de la clé complète. Le clair n'est jamais persisté.</summary>
    public string KeyHash { get; private set; } = string.Empty;

    public bool IsRevoked { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Horodatage du dernier heartbeat reçu (UTC), ou <c>null</c> si l'agent ne s'est jamais signalé.</summary>
    public DateTimeOffset? LastSeenAtUtc { get; private set; }

    /// <summary>Dernière version d'agent vue (alimentée par le heartbeat), ou <c>null</c>.</summary>
    public string? LastAgentVersion { get; private set; }

    /// <summary>
    /// Enregistre un nouvel agent pour un tenant et génère sa clé API. Renvoie l'entité (à persister)
    /// et la clé COMPLÈTE (à afficher une seule fois à l'opérateur — jamais persistée en clair).
    /// </summary>
    public static (Agent Agent, string FullKey) Create(string tenantId, string name)
    {
        ValidateTenantId(tenantId);
        ValidateName(name);

        var material = AgentKey.Generate();
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId.Trim(),
            Name = name.Trim(),
            KeyPrefix = material.Prefix,
            KeyHash = material.Hash,
            IsRevoked = false,
            CreatedAt = DateTimeOffset.UtcNow,
            RevokedAt = null,
            LastSeenAtUtc = null,
            LastAgentVersion = null,
        };

        return (agent, material.FullKey);
    }

    /// <summary>
    /// Extrait le préfixe d'une clé présentée (<c>prefix.secret</c>) pour la résolution en base.
    /// </summary>
    public static bool TryExtractKeyPrefix(string? presentedFullKey, out string prefix) =>
        AgentKey.TryExtractPrefix(presentedFullKey, out prefix);

    public static Agent Reconstitute(
        Guid id,
        string tenantId,
        string name,
        string keyPrefix,
        string keyHash,
        bool isRevoked,
        DateTimeOffset createdAt,
        DateTimeOffset? revokedAt,
        DateTimeOffset? lastSeenAtUtc,
        string? lastAgentVersion)
    {
        return new Agent
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            KeyPrefix = keyPrefix,
            KeyHash = keyHash,
            IsRevoked = isRevoked,
            CreatedAt = createdAt,
            RevokedAt = revokedAt,
            LastSeenAtUtc = lastSeenAtUtc,
            LastAgentVersion = lastAgentVersion,
        };
    }

    /// <summary>
    /// Génère une nouvelle clé API pour cet agent (rotation) et renvoie la clé COMPLÈTE (affichée
    /// une fois). L'ancienne clé cesse immédiatement d'être valide. Un agent révoqué ne peut pas
    /// faire pivoter sa clé (on réenregistre plutôt un nouvel agent).
    /// </summary>
    public string RotateKey()
    {
        if (IsRevoked)
        {
            throw new ConflictException("Impossible de faire pivoter la clé d'un agent révoqué.");
        }

        var material = AgentKey.Generate();
        KeyPrefix = material.Prefix;
        KeyHash = material.Hash;
        return material.FullKey;
    }

    /// <summary>Révoque l'agent : sa clé est immédiatement refusée (401/403). Idempotent.</summary>
    public void Revoke()
    {
        if (IsRevoked)
        {
            return;
        }

        IsRevoked = true;
        RevokedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Enregistre la réception d'un heartbeat : met à jour la dernière vue et la version d'agent.</summary>
    public void RecordHeartbeat(string agentVersion, DateTimeOffset seenAtUtc)
    {
        if (!string.IsNullOrWhiteSpace(agentVersion))
        {
            LastAgentVersion = agentVersion.Trim();
        }

        LastSeenAtUtc = seenAtUtc;
    }

    /// <summary>
    /// Vérifie qu'une clé présentée correspond à cet agent (comparaison d'empreinte en temps constant).
    /// Ne dit rien de l'état de révocation — c'est l'authentificateur qui l'évalue.
    /// </summary>
    public bool MatchesPresentedKey(string presentedFullKey) => AgentKey.HashesMatch(KeyHash, presentedFullKey);

    private static void ValidateTenantId(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Le tenant de l'agent est obligatoire.", nameof(tenantId));
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Le nom de l'agent est obligatoire.", nameof(name));
        }
    }
}
