namespace Liakont.Modules.TenantSettings.Infrastructure.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Liakont.Modules.TvaMapping.Contracts.Queries;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Composition de la vue de paramétrage de la console (API01c). Lit les read-models du propre module
/// (<see cref="ITenantSettingsQueries"/>), l'état de la table TVA (<see cref="ITvaMappingQueries"/>) et
/// les capacités déclarées des PA configurées (<see cref="IPaClientRegistry"/>) — tous par leurs
/// Contracts (frontière inter-modules, CLAUDE.md n°14 ; même patron cross-module que les services
/// d'export d'Archive). Tenant-scopée : les lectures passent par le <c>companyId</c> du tenant courant
/// (database-per-tenant), et le slug du tenant (<see cref="ITenantContext"/>) sert à résoudre le plug-in
/// PA. N'expose JAMAIS de secret (les comptes PA arrivent déjà masqués — INV-TENANTSETTINGS-003) ni de
/// capacité fiscale inventée (les capacités sont DÉCLARÉES par le plug-in — CLAUDE.md n°2/8).
/// </summary>
public sealed class TenantSettingsConsoleQueries : ITenantSettingsConsoleQueries
{
    private static readonly TenantSettingsOverviewDto EmptyOverview = new()
    {
        Profile = null,
        FiscalSettings = null,
        TvaMapping = null,
        PaAccounts = [],
    };

    private readonly ITenantSettingsQueries _settings;
    private readonly ITvaMappingQueries _tvaMapping;
    private readonly IPaClientRegistry _paRegistry;
    private readonly ITenantContext _tenantContext;

    /// <summary>Construit la composition à partir des read-models et du registre de plug-ins PA.</summary>
    /// <param name="settings">Lectures du paramétrage du propre module.</param>
    /// <param name="tvaMapping">Lecture de la table de mapping TVA (module TvaMapping).</param>
    /// <param name="paRegistry">Registre des plug-ins PA (module Transmission) — pour les capacités déclarées.</param>
    /// <param name="tenantContext">Contexte du tenant courant (slug), résolu par le middleware.</param>
    public TenantSettingsConsoleQueries(
        ITenantSettingsQueries settings,
        ITvaMappingQueries tvaMapping,
        IPaClientRegistry paRegistry,
        ITenantContext tenantContext)
    {
        _settings = settings;
        _tvaMapping = tvaMapping;
        _paRegistry = paRegistry;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<TenantSettingsOverviewDto> GetSettingsOverview(CancellationToken ct = default)
    {
        var companyId = await _settings.GetCurrentCompanyId(ct);
        if (companyId is null)
        {
            // Profil tenant pas encore créé (CFG02) : vue vide en 200 (transitoire), jamais une erreur.
            return EmptyOverview;
        }

        var profile = await _settings.GetTenantProfile(companyId.Value, ct);
        var fiscal = await _settings.GetFiscalSettings(companyId.Value, ct);
        var accounts = await _settings.GetPaAccounts(companyId.Value, ct);
        var mappingTable = await _tvaMapping.GetMappingTable(companyId.Value, ct);

        var paAccounts = new List<PaAccountSettingsDto>(accounts.Count);
        foreach (var account in accounts)
        {
            paAccounts.Add(BuildPaAccountSettings(account));
        }

        return new TenantSettingsOverviewDto
        {
            Profile = profile,
            FiscalSettings = fiscal,
            TvaMapping = mappingTable is null ? null : ToSummary(mappingTable),
            PaAccounts = paAccounts,
        };
    }

    private static TvaMappingSummaryDto ToSummary(MappingTableDto table) => new()
    {
        MappingVersion = table.MappingVersion,
        IsValidated = table.IsValidated,
        ValidatedBy = table.ValidatedBy,
        ValidatedDate = table.ValidatedDate,
        DefaultBehavior = table.DefaultBehavior,
        RuleCount = table.Rules.Count,
    };

    private static PaCapabilitiesSummaryDto ToSummary(PaCapabilities capabilities) => new()
    {
        PaName = capabilities.PaName,
        SupportsB2cReporting = capabilities.SupportsB2cReporting,
        SupportsDomesticPaymentReporting = capabilities.SupportsDomesticPaymentReporting,
        SupportsInternationalPaymentReporting = capabilities.SupportsInternationalPaymentReporting,
        SupportsB2bInvoicing = capabilities.SupportsB2bInvoicing,
        SupportsCreditNotes = capabilities.SupportsCreditNotes,
        SupportsTaxReportRetrieval = capabilities.SupportsTaxReportRetrieval,
        SupportsDocumentRetrieval = capabilities.SupportsDocumentRetrieval,
        SupportsReportRectification = capabilities.SupportsReportRectification,
        MaxDocumentsPerRequest = capabilities.MaxDocumentsPerRequest,
    };

    /// <summary>
    /// Enrichit un compte PA de ses capacités déclarées. Si aucun plug-in n'est chargé pour le type du
    /// compte (ou si le tenant n'est pas résolu), renvoie <c>PluginAvailable = false</c> + capacités
    /// <c>null</c> : la lecture reste valide et signale le défaut de configuration plutôt que d'échouer,
    /// sans inventer de capacité (CLAUDE.md n°3/8).
    /// </summary>
    private PaAccountSettingsDto BuildPaAccountSettings(PaAccountDto account)
    {
        var tenantId = _tenantContext.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId) || !_paRegistry.IsRegistered(account.PluginType))
        {
            return new PaAccountSettingsDto { Account = account, PluginAvailable = false, Capabilities = null };
        }

        var capabilities = _paRegistry.Resolve(new PaAccountDescriptor(account.PluginType, tenantId)).Capabilities;
        return new PaAccountSettingsDto
        {
            Account = account,
            PluginAvailable = true,
            Capabilities = ToSummary(capabilities),
        };
    }
}
