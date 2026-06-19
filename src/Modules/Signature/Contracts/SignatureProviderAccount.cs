namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// Description du compte de signature d'un tenant, utilisée pour RÉSOUDRE le bon plug-in (ADR-0027 §4).
/// Calqué EXACTEMENT sur <c>PaAccountDescriptor</c> : porte le TYPE de plug-in (clé de registre, ex.
/// « Yousign ») + une configuration NON sensible. NE PORTE AUCUN SECRET EN CLAIR : les secrets (clé API,
/// secret de webhook) sont chiffrés par tenant et résolus EN INTERNE par le plug-in (SIG07), via le
/// coffre du tenant (CLAUDE.md n°10). L'URL de base n'est jamais un champ libre ici — un plug-in la
/// dérive d'une allowlist par environnement (anti-SSRF, SIG07).
/// </summary>
public sealed record SignatureProviderAccount
{
    /// <summary>Crée une description de compte de signature.</summary>
    /// <param name="providerType">Type de plug-in (clé de registre, insensible à la casse). Jamais vide.</param>
    /// <param name="companyId">Tenant propriétaire du compte (clé <c>company_id</c>). Jamais vide.</param>
    /// <param name="environment">Environnement déclaré (ex. « sandbox », « production »), ou <c>null</c>.</param>
    /// <param name="settings">Configuration non sensible du plug-in (identifiants de compte…). Jamais de secret en clair.</param>
    public SignatureProviderAccount(
        string providerType,
        string companyId,
        string? environment = null,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        if (string.IsNullOrWhiteSpace(providerType))
        {
            throw new ArgumentException("Le type de fournisseur de signature est obligatoire.", nameof(providerType));
        }

        if (string.IsNullOrWhiteSpace(companyId))
        {
            throw new ArgumentException("Le tenant (company_id) est obligatoire.", nameof(companyId));
        }

        ProviderType = providerType;
        CompanyId = companyId;
        Environment = environment;
        Settings = settings ?? new Dictionary<string, string>();
    }

    /// <summary>Type de plug-in (clé de registre, ex. « Yousign », « Wacom »).</summary>
    public string ProviderType { get; }

    /// <summary>Tenant propriétaire du compte (clé <c>company_id</c>).</summary>
    public string CompanyId { get; }

    /// <summary>Environnement déclaré (ex. « sandbox », « production »), ou <c>null</c>.</summary>
    public string? Environment { get; }

    /// <summary>Configuration non sensible du plug-in (jamais de secret en clair). Jamais <c>null</c>.</summary>
    public IReadOnlyDictionary<string, string> Settings { get; }
}
