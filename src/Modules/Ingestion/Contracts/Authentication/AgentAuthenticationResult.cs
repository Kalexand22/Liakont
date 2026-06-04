namespace Liakont.Modules.Ingestion.Contracts.Authentication;

/// <summary>Issue de l'authentification d'une clé API d'agent (F12 §3.3).</summary>
public enum AgentAuthenticationOutcome
{
    /// <summary>Clé valide et agent actif : <see cref="AgentAuthenticationResult.Identity"/> est renseignée.</summary>
    Authenticated = 0,

    /// <summary>Clé absente, mal formée, inconnue ou empreinte non concordante → 401.</summary>
    InvalidKey = 1,

    /// <summary>Clé connue mais agent révoqué → 403.</summary>
    Revoked = 2,
}

/// <summary>
/// Résultat de l'authentification d'une clé API d'agent. On ne distingue jamais « préfixe inconnu »
/// de « empreinte non concordante » côté appelant (les deux → <see cref="AgentAuthenticationOutcome.InvalidKey"/>) :
/// révéler la différence faciliterait l'énumération des clés.
/// </summary>
public sealed class AgentAuthenticationResult
{
    private AgentAuthenticationResult(AgentAuthenticationOutcome outcome, AgentIdentity? identity)
    {
        Outcome = outcome;
        Identity = identity;
    }

    public AgentAuthenticationOutcome Outcome { get; }

    /// <summary>Identité résolue si <see cref="Outcome"/> = <see cref="AgentAuthenticationOutcome.Authenticated"/>, sinon <c>null</c>.</summary>
    public AgentIdentity? Identity { get; }

    public static AgentAuthenticationResult Authenticated(AgentIdentity identity) =>
        new(AgentAuthenticationOutcome.Authenticated, identity);

    public static AgentAuthenticationResult InvalidKey() =>
        new(AgentAuthenticationOutcome.InvalidKey, null);

    public static AgentAuthenticationResult Revoked() =>
        new(AgentAuthenticationOutcome.Revoked, null);
}
