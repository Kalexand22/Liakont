namespace Liakont.PaClients.B2Brouter;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Configuration de comportement d'un <see cref="B2BrouterClient"/> (NON sensible) : identifiant de
/// compte (segment d'URL des endpoints) + capacités déclarées. L'URL de base, la clé API et la
/// version sont portées par le <see cref="System.Net.Http.HttpClient"/> configuré par la fabrique
/// (en-têtes par défaut) — le client ne manipule jamais la clé en clair (CLAUDE.md n°10).
/// </summary>
internal sealed record B2BrouterClientOptions
{
    /// <summary>Crée les options du client.</summary>
    /// <param name="accountId">Identifiant de compte B2Brouter (segment d'URL). Obligatoire.</param>
    /// <param name="capabilities">Capacités déclarées ; <c>null</c> = <see cref="B2BrouterCapabilities.Declared"/>.</param>
    public B2BrouterClientOptions(string accountId, PaCapabilities? capabilities = null)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new ArgumentException("L'identifiant de compte B2Brouter est obligatoire.", nameof(accountId));
        }

        AccountId = accountId;
        Capabilities = capabilities ?? B2BrouterCapabilities.Declared;
    }

    /// <summary>Identifiant de compte B2Brouter (segment d'URL des endpoints).</summary>
    public string AccountId { get; }

    /// <summary>Capacités déclarées de ce client.</summary>
    public PaCapabilities Capabilities { get; }
}
