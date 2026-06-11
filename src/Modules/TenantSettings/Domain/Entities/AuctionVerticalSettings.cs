namespace Liakont.Modules.TenantSettings.Domain.Entities;

/// <summary>
/// Activation du vertical « vente aux enchères » d'un tenant (paramétrage PRODUIT, décision opérateur
/// D4 du 2026-06-11, lot FIX03). Quand elle est active, le découpage Adjudication / Frais d'une règle de
/// mapping TVA (F03 §2.3, <c>MappingPart</c>) devient pertinent et l'éditeur de règle expose le champ
/// « part » ; sinon il reste masqué et la part <c>Autre</c> est implicite. Paramétrable par tenant,
/// défaut <b>OFF</b> (jamais une activation implicite — blueprint §2 règle 7) : un tenant générique n'est
/// jamais encombré du vocabulaire enchères. Ce n'est PAS un flag dupliquant une capacité PA (CLAUDE.md
/// n°8) : il décrit le métier du tenant, indépendamment de toute plateforme.
/// </summary>
public sealed class AuctionVerticalSettings
{
    /// <summary>Valeur par défaut produit : vertical enchères DÉSACTIVÉ (D4).</summary>
    public const bool DefaultEnabled = false;

    private AuctionVerticalSettings()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary><c>true</c> si le vertical enchères est activé pour ce tenant ; défaut <c>false</c>.</summary>
    public bool Enabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Crée l'activation avec la valeur par défaut produit (vertical désactivé — D4).</summary>
    public static AuctionVerticalSettings CreateDefault(Guid companyId) => Create(companyId, DefaultEnabled);

    public static AuctionVerticalSettings Create(Guid companyId, bool enabled)
    {
        return new AuctionVerticalSettings
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Enabled = enabled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
        };
    }

    public static AuctionVerticalSettings Reconstitute(
        Guid id,
        Guid companyId,
        bool enabled,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new AuctionVerticalSettings
        {
            Id = id,
            CompanyId = companyId,
            Enabled = enabled,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    public void Update(bool enabled)
    {
        Enabled = enabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
