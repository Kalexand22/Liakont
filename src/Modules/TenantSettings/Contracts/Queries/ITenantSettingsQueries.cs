namespace Liakont.Modules.TenantSettings.Contracts.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Lectures du paramétrage tenant. Chaque méthode est scopée par <paramref name="companyId"/>
/// (résolu par le contexte appelant via <c>ICompanyFilter</c>) — jamais de lecture cross-tenant
/// (CLAUDE.md n°9/17). Les implémentations n'exposent JAMAIS de clé API (INV-TENANTSETTINGS-003).
/// </summary>
public interface ITenantSettingsQueries
{
    Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default);

    Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Lit les mentions de facturation du tenant (F12-A §3.4, BUG-26) : termes de paiement (BT-20) +
    /// mentions légales FR (BR-FR-05). <c>null</c> tant qu'aucune mention n'est définie.
    /// </summary>
    Task<BillingMentionsDto?> GetBillingMentions(Guid companyId, CancellationToken ct = default);

    Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default);

    Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default);

    Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Indique si le vertical « vente aux enchères » est activé pour le tenant (paramétrage produit,
    /// décision opérateur D4, lot FIX03). Défaut produit <c>false</c> : une ligne absente vaut « OFF »
    /// (jamais une activation implicite — blueprint §2 règle 7).
    /// </summary>
    Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Identifiant de l'UNIQUE société du tenant courant, résolu SANS <c>companyId</c> (depuis
    /// <c>tenantsettings.tenant_profiles</c>, où <c>company_id</c> est unique par base — database-per-tenant,
    /// blueprint §7). <c>null</c> tant que le profil du tenant n'est pas créé (CFG02). Permet à un job tenant
    /// (pipeline, paiements) d'obtenir le <c>companyId</c> requis par les autres lectures sans lire la table
    /// d'un autre module en SQL brut (frontière, CLAUDE.md n°14).
    /// </summary>
    Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default);

    /// <summary>
    /// Statut du tenant COURANT (« Actif » / « Suspendu »), résolu SANS <c>companyId</c> (même patron
    /// que <see cref="GetCurrentCompanyId"/> — au plus une ligne de profil par base). <c>null</c> =
    /// aucun profil : le tenant est réputé ACTIF (jamais de suspension implicite — un tenant jamais
    /// seedé continue d'accepter ses agents). Sert l'application du statut (OPS03 lot B : refus de
    /// push agent et de connexion d'un tenant suspendu).
    /// </summary>
    Task<string?> GetCurrentTenantStatut(CancellationToken ct = default);
}
