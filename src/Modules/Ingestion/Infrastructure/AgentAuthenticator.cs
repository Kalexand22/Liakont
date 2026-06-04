namespace Liakont.Modules.Ingestion.Infrastructure;

using Dapper;
using Liakont.Modules.Ingestion.Contracts.Authentication;
using Liakont.Modules.Ingestion.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Authentifie une clé API d'agent contre le registre SYSTÈME (F12 §3.1). Lecture seule sans
/// transaction (l'authentification précède tout contexte tenant). Au niveau de l'ISSUE, « préfixe
/// inconnu » et « empreinte non concordante » sont indistinguables (les deux → InvalidKey). Le coût
/// dominant (calcul SHA-256 + comparaison à temps constant) est égalisé entre ces deux chemins via
/// <see cref="Agent.SpendKeyComparisonTime"/> ; une différence résiduelle de temps d'accès en base
/// subsiste, rendue non exploitable par le préfixe aléatoire (~9 octets) et le rate limiting par IP.
/// </summary>
internal sealed class AgentAuthenticator : IAgentAuthenticator
{
    private readonly ISystemConnectionFactory _systemConnectionFactory;

    public AgentAuthenticator(ISystemConnectionFactory systemConnectionFactory)
    {
        _systemConnectionFactory = systemConnectionFactory;
    }

    public async Task<AgentAuthenticationResult> AuthenticateAsync(string? presentedKey, CancellationToken cancellationToken = default)
    {
        if (!Agent.TryExtractKeyPrefix(presentedKey, out var prefix))
        {
            return AgentAuthenticationResult.InvalidKey();
        }

        var sql = $"SELECT {AgentRowMapper.Columns} FROM ingestion.agents WHERE key_prefix = @KeyPrefix";

        using var connection = await _systemConnectionFactory.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { KeyPrefix = prefix }, cancellationToken: cancellationToken));

        if (row is null)
        {
            // Égalise le temps de réponse avec le chemin « préfixe connu » (calcul d'empreinte) afin
            // de ne pas révéler par le timing l'existence d'un préfixe (anti-énumération).
            Agent.SpendKeyComparisonTime(presentedKey!);
            return AgentAuthenticationResult.InvalidKey();
        }

        var agent = AgentRowMapper.Map(row);

        // L'empreinte est vérifiée avant l'état de révocation : une clé non concordante reste un
        // 401 (clé invalide), jamais un 403 (qui révélerait l'existence de l'agent).
        if (!agent.MatchesPresentedKey(presentedKey!))
        {
            return AgentAuthenticationResult.InvalidKey();
        }

        if (agent.IsRevoked)
        {
            return AgentAuthenticationResult.Revoked();
        }

        return AgentAuthenticationResult.Authenticated(new AgentIdentity(agent.Id, agent.TenantId, agent.Name));
    }
}
