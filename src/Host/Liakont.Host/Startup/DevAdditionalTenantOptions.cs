namespace Liakont.Host.Startup;

/// <summary>
/// Tenant de recette additionnel (au-delà du tenant système <c>default</c>), amorcé dans
/// <c>outbox.tenants</c> AVEC son <c>company_id</c>. RLM01 : un 2e tenant au <c>company_id</c> DISTINCT
/// rend l'isolation par claim prouvable de bout en bout (deux utilisateurs réels → deux <c>company_id</c>
/// différents). Contrairement au tenant <c>default</c> (dont le <c>company_id</c> est backfillé en RLM02),
/// le company_id d'un tenant additionnel est posé dès l'amorçage car c'est sa raison d'être.
/// </summary>
internal sealed class DevAdditionalTenantOptions
{
    /// <summary>Identifiant du tenant additionnel (ex. <c>tenant2</c>). Vide = entrée ignorée.</summary>
    public string TenantId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string AdminEmail { get; init; } = string.Empty;

    /// <summary>
    /// Realm rattaché. En realm UNIQUE partagé (ADR-0021) c'est le même realm que <c>default</c>, mais
    /// <c>outbox.tenants.realm_name</c> reste sous contrainte UNIQUE (vestigial, retiré en RLM04) : on pose
    /// donc un placeholder distinct. La résolution autoritaire passe par <c>company_id</c> (RLM02), pas par
    /// ce champ.
    /// </summary>
    public string RealmName { get; init; } = string.Empty;

    public string DatabaseName { get; init; } = string.Empty;

    public string? ClientSecret { get; init; }

    /// <summary>Société (company_id) DISTINCTE du tenant. <see cref="System.Guid.Empty"/> = entrée ignorée.</summary>
    public System.Guid CompanyId { get; init; }
}
