namespace Liakont.PaClients.SuperPdp;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Configuration de comportement d'un <see cref="SuperPdpClient"/> (NON sensible) : identifiant de
/// compte (audit) + capacités déclarées + politique de retry. L'URL de base et l'authentification OAuth
/// (jeton bearer) sont portées par le <see cref="ISuperPdpTokenProvider"/> et l'<c>HttpClient</c>
/// configuré par la fabrique — le client ne manipule jamais les secrets en clair (CLAUDE.md n°10).
/// </summary>
internal sealed record SuperPdpClientOptions
{
    /// <summary>Crée les options du client.</summary>
    /// <param name="accountId">Identifiant de compte Super PDP (audit). Obligatoire.</param>
    /// <param name="capabilities">Capacités déclarées ; <c>null</c> = <see cref="SuperPdpCapabilities.Declared"/>.</param>
    /// <param name="retryPolicy">
    /// Politique de retry des erreurs transitoires ; <c>null</c> = <see cref="SuperPdpRetryPolicy.Default"/>.
    /// Surchargée uniquement par les tests (backoff zéro).
    /// </param>
    public SuperPdpClientOptions(
        string accountId,
        PaCapabilities? capabilities = null,
        SuperPdpRetryPolicy? retryPolicy = null)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new ArgumentException("L'identifiant de compte Super PDP est obligatoire.", nameof(accountId));
        }

        AccountId = accountId;
        Capabilities = capabilities ?? SuperPdpCapabilities.Declared;
        RetryPolicy = retryPolicy ?? SuperPdpRetryPolicy.Default;
    }

    /// <summary>Identifiant de compte Super PDP (audit).</summary>
    public string AccountId { get; }

    /// <summary>Capacités déclarées de ce client.</summary>
    public PaCapabilities Capabilities { get; }

    /// <summary>Politique de retry des erreurs transitoires.</summary>
    public SuperPdpRetryPolicy RetryPolicy { get; }
}
