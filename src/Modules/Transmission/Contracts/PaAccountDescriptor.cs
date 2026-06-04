namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Description du compte PA d'un tenant utilisée pour RÉSOUDRE le bon plug-in (F05 ; PAA01 §5). Porte
/// le TYPE de plug-in (clé de registre, ex. « B2Brouter ») et la configuration spécifique au plug-in.
/// NE PORTE AUCUN SECRET EN CLAIR (clé API, mot de passe) : les secrets sont chiffrés par tenant et
/// résolus par le plug-in via le coffre du module Identity/TenantSettings (CLAUDE.md n°10) — ce DTO
/// ne transporte que des identifiants non sensibles (account id, URL de base…).
/// </summary>
public sealed record PaAccountDescriptor
{
    /// <summary>Crée une description de compte PA.</summary>
    /// <param name="paType">Type de plug-in (clé de registre, insensible à la casse). Jamais vide.</param>
    /// <param name="tenantId">Tenant propriétaire du compte (slug). Jamais vide.</param>
    /// <param name="settings">
    /// Configuration non sensible du plug-in (account id, URL…). Jamais de secret en clair.
    /// </param>
    public PaAccountDescriptor(
        string paType,
        string tenantId,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        if (string.IsNullOrWhiteSpace(paType))
        {
            throw new ArgumentException("Le type de PA est obligatoire.", nameof(paType));
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Le tenant est obligatoire.", nameof(tenantId));
        }

        PaType = paType;
        TenantId = tenantId;
        Settings = settings ?? new Dictionary<string, string>();
    }

    /// <summary>Type de plug-in (clé de registre, ex. « B2Brouter », « SuperPdp », « Fake »).</summary>
    public string PaType { get; }

    /// <summary>Tenant propriétaire du compte (slug).</summary>
    public string TenantId { get; }

    /// <summary>Configuration non sensible du plug-in (jamais de secret en clair). Jamais <c>null</c>.</summary>
    public IReadOnlyDictionary<string, string> Settings { get; }
}
