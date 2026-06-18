namespace Liakont.Modules.TenantSettings.Domain.Entities;

/// <summary>
/// Compte Plateforme Agréée d'un tenant (F12-A §4). Un tenant peut en détenir plusieurs
/// (staging + production, ou multi-PA).
/// </summary>
/// <remarks>
/// <strong>INV-TENANTSETTINGS-003 :</strong> la clé API n'est JAMAIS stockée en clair. Le domaine
/// ne manipule que <see cref="EncryptedApiKey"/> (texte chiffré opaque, produit par
/// <c>ISecretProtector</c> hors du domaine). Le clair n'entre jamais dans le domaine, ni dans un
/// DTO de lecture, ni dans un log (CLAUDE.md n°10). <c>null</c> = aucune clé saisie (à compléter
/// via la console). Le <c>pluginType</c> est un identifiant de plug-in, jamais un flag produit
/// (CLAUDE.md n°6/8).
/// </remarks>
public sealed class PaAccount
{
    private PaAccount()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string PluginType { get; private set; } = string.Empty;

    public PaEnvironment Environment { get; private set; }

    /// <summary>Identifiants de compte côté PA (non secrets), opaques au produit (JSON sérialisé).</summary>
    public string AccountIdentifiers { get; private set; } = string.Empty;

    /// <summary>Clé API CHIFFRÉE (texte opaque), ou <c>null</c> si aucune clé n'a encore été saisie.</summary>
    public string? EncryptedApiKey { get; private set; }

    /// <summary>« client_id » OAuth2 CHIFFRÉ (texte opaque), ou <c>null</c> si non saisi (plug-in en OAuth2 uniquement).</summary>
    public string? EncryptedClientId { get; private set; }

    /// <summary>« client_secret » OAuth2 CHIFFRÉ (texte opaque), ou <c>null</c> si non saisi (plug-in en OAuth2 uniquement).</summary>
    public string? EncryptedClientSecret { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static PaAccount Create(
        Guid companyId,
        string pluginType,
        PaEnvironment environment,
        string accountIdentifiers,
        string? encryptedApiKey,
        string? encryptedClientId = null,
        string? encryptedClientSecret = null)
    {
        ValidatePluginType(pluginType);

        return new PaAccount
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            PluginType = pluginType.Trim(),
            Environment = environment,
            AccountIdentifiers = accountIdentifiers ?? string.Empty,
            EncryptedApiKey = encryptedApiKey,
            EncryptedClientId = encryptedClientId,
            EncryptedClientSecret = encryptedClientSecret,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
        };
    }

    public static PaAccount Reconstitute(
        Guid id,
        Guid companyId,
        string pluginType,
        PaEnvironment environment,
        string accountIdentifiers,
        string? encryptedApiKey,
        string? encryptedClientId,
        string? encryptedClientSecret,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new PaAccount
        {
            Id = id,
            CompanyId = companyId,
            PluginType = pluginType,
            Environment = environment,
            AccountIdentifiers = accountIdentifiers,
            EncryptedApiKey = encryptedApiKey,
            EncryptedClientId = encryptedClientId,
            EncryptedClientSecret = encryptedClientSecret,
            IsActive = isActive,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    /// <summary>Met à jour l'environnement et les identifiants. La clé API se change via <see cref="SetEncryptedApiKey"/>.</summary>
    public void UpdateDetails(PaEnvironment environment, string accountIdentifiers)
    {
        Environment = environment;
        AccountIdentifiers = accountIdentifiers ?? string.Empty;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Remplace la clé API chiffrée. La valeur reçue est DÉJÀ chiffrée (texte opaque) :
    /// le domaine ne chiffre pas et ne voit jamais le clair. <c>null</c> efface la clé.
    /// </summary>
    public void SetEncryptedApiKey(string? encryptedApiKey)
    {
        EncryptedApiKey = encryptedApiKey;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Remplace le « client_id » OAuth2 chiffré (valeur DÉJÀ chiffrée, texte opaque). <c>null</c> efface.
    /// </summary>
    public void SetEncryptedClientId(string? encryptedClientId)
    {
        EncryptedClientId = encryptedClientId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Remplace le « client_secret » OAuth2 chiffré (valeur DÉJÀ chiffrée, texte opaque). <c>null</c> efface.
    /// </summary>
    public void SetEncryptedClientSecret(string? encryptedClientSecret)
    {
        EncryptedClientSecret = encryptedClientSecret;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidatePluginType(string pluginType)
    {
        if (string.IsNullOrWhiteSpace(pluginType))
        {
            throw new ArgumentException(
                "INV-TENANTSETTINGS-002 : le type de plug-in PA est obligatoire.",
                nameof(pluginType));
        }
    }
}
