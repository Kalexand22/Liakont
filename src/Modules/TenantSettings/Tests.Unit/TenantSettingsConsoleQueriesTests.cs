namespace Liakont.Modules.TenantSettings.Tests.Unit;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TenantSettings.Infrastructure.Queries;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Liakont.Modules.TvaMapping.Contracts.Queries;
using Liakont.PaClients.Fake;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

/// <summary>
/// Tests unitaires de la composition de la vue de paramétrage de la console (API01c,
/// <see cref="TenantSettingsConsoleQueries"/>) : vue vide tant que le profil n'existe pas, projection de
/// l'état de la table TVA, exposition des capacités déclarées d'une PA dont le plug-in est chargé,
/// indisponibilité (sans capacité inventée) d'un type de PA non enregistré, et passage des comptes PA
/// déjà masqués (HasApiKey uniquement).
/// </summary>
public sealed class TenantSettingsConsoleQueriesTests
{
    private const string TenantSlug = "tenant-x";

    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task GetSettingsOverview_Returns_Empty_When_No_Company_Profile()
    {
        var sut = new TenantSettingsConsoleQueries(
            new StubTenantSettingsQueries { CompanyId = null },
            new StubTvaMappingQueries(),
            new StubPaClientRegistry(),
            new StubTenantContext(TenantSlug),
            NullLogger<TenantSettingsConsoleQueries>.Instance);

        var overview = await sut.GetSettingsOverview();

        overview.Profile.Should().BeNull();
        overview.FiscalSettings.Should().BeNull();
        overview.TvaMapping.Should().BeNull();
        overview.PaAccounts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSettingsOverview_Projects_Tva_Mapping_State()
    {
        var settings = new StubTenantSettingsQueries { CompanyId = CompanyId };
        var tva = new StubTvaMappingQueries
        {
            Table = new MappingTableDto
            {
                Id = Guid.NewGuid(),
                CompanyId = CompanyId,
                MappingVersion = "v7",
                ValidatedBy = "Expert",
                ValidatedDate = new DateOnly(2026, 4, 2),
                IsValidated = true,
                DefaultBehavior = "Block",
                Rules = new[] { Rule("REGIME-A"), Rule("REGIME-B") },
                CreatedAt = DateTimeOffset.UnixEpoch,
            },
        };

        var sut = new TenantSettingsConsoleQueries(settings, tva, new StubPaClientRegistry(), new StubTenantContext(TenantSlug), NullLogger<TenantSettingsConsoleQueries>.Instance);

        var overview = await sut.GetSettingsOverview();

        overview.TvaMapping.Should().NotBeNull();
        overview.TvaMapping!.MappingVersion.Should().Be("v7");
        overview.TvaMapping.IsValidated.Should().BeTrue();
        overview.TvaMapping.ValidatedBy.Should().Be("Expert");
        overview.TvaMapping.ValidatedDate.Should().Be(new DateOnly(2026, 4, 2));
        overview.TvaMapping.DefaultBehavior.Should().Be("Block");
        overview.TvaMapping.RuleCount.Should().Be(2);
    }

    [Fact]
    public async Task GetSettingsOverview_Exposes_Declared_Capabilities_For_Registered_Plugin()
    {
        var capabilities = new PaCapabilities
        {
            PaName = "PA Test",
            SupportsB2cReporting = true,
            SupportsCreditNotes = true,
            SupportsReportRectification = true,
            MaxDocumentsPerRequest = 25,
        };
        var settings = new StubTenantSettingsQueries
        {
            CompanyId = CompanyId,
            Accounts = new[] { Account("Fake", hasApiKey: true) },
        };
        var registry = new StubPaClientRegistry { CapabilitiesByType = { ["Fake"] = capabilities } };

        var sut = new TenantSettingsConsoleQueries(settings, new StubTvaMappingQueries(), registry, new StubTenantContext(TenantSlug), NullLogger<TenantSettingsConsoleQueries>.Instance);

        var overview = await sut.GetSettingsOverview();

        var account = overview.PaAccounts.Should().ContainSingle().Subject;
        account.PluginAvailable.Should().BeTrue();
        account.Account.HasApiKey.Should().BeTrue();
        account.Capabilities.Should().NotBeNull();
        account.Capabilities!.PaName.Should().Be("PA Test");
        account.Capabilities.SupportsCreditNotes.Should().BeTrue();
        account.Capabilities.SupportsReportRectification.Should().BeTrue();
        account.Capabilities.MaxDocumentsPerRequest.Should().Be(25);
    }

    [Fact]
    public async Task GetSettingsOverview_Marks_Unregistered_Plugin_Unavailable_Without_Inventing_Capabilities()
    {
        var settings = new StubTenantSettingsQueries
        {
            CompanyId = CompanyId,
            Accounts = new[] { Account("UnknownPa", hasApiKey: false) },
        };

        // Registre vide : aucun plug-in chargé pour « UnknownPa ».
        var sut = new TenantSettingsConsoleQueries(settings, new StubTvaMappingQueries(), new StubPaClientRegistry(), new StubTenantContext(TenantSlug), NullLogger<TenantSettingsConsoleQueries>.Instance);

        var overview = await sut.GetSettingsOverview();

        var account = overview.PaAccounts.Should().ContainSingle().Subject;
        account.PluginAvailable.Should().BeFalse();
        account.Capabilities.Should().BeNull();
        account.Account.HasApiKey.Should().BeFalse();
    }

    [Fact]
    public async Task GetSettingsOverview_Degrades_When_Plugin_Resolution_Throws()
    {
        var settings = new StubTenantSettingsQueries
        {
            CompanyId = CompanyId,
            Accounts = new[] { Account("B2Brouter", hasApiKey: false) },
        };

        // Plug-in enregistré mais dont Resolve lève (clé PA non encore saisie — INV-TENANTSETTINGS-007).
        var registry = new StubPaClientRegistry { UnresolvableTypes = { "B2Brouter" } };

        var sut = new TenantSettingsConsoleQueries(
            settings,
            new StubTvaMappingQueries(),
            registry,
            new StubTenantContext(TenantSlug),
            NullLogger<TenantSettingsConsoleQueries>.Instance);

        var overview = await sut.GetSettingsOverview();

        var account = overview.PaAccounts.Should().ContainSingle().Subject;
        account.PluginAvailable.Should().BeFalse("la résolution du plug-in a échoué — la lecture ne doit jamais renvoyer 500");
        account.Capabilities.Should().BeNull("aucune capacité n'est inventée quand le plug-in ne peut être résolu");
    }

    private static MappingRuleDto Rule(string regime) => new()
    {
        SourceRegimeCode = regime,
        Part = "Adjudication",
        Category = "S",
        RateMode = "Fixed",
        RateValue = 20m,
    };

    private static PaAccountDto Account(string pluginType, bool hasApiKey) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = CompanyId,
        PluginType = pluginType,
        Environment = "Staging",
        AccountIdentifiers = "acct",
        HasApiKey = hasApiKey,
        IsActive = true,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class StubTenantSettingsQueries : ITenantSettingsQueries
    {
        public Guid? CompanyId { get; init; }

        public TenantProfileDto? Profile { get; init; }

        public FiscalSettingsDto? Fiscal { get; init; }

        public IReadOnlyList<PaAccountDto> Accounts { get; init; } = [];

        public Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) => Task.FromResult(Profile);

        public Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) => Task.FromResult(Fiscal);

        public Task<BillingMentionsDto?> GetBillingMentions(Guid companyId, CancellationToken ct = default) => Task.FromResult<BillingMentionsDto?>(null);

        public Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) => Task.FromResult(Accounts);

        public Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) => Task.FromResult<ExtractionScheduleDto?>(null);

        public Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) => Task.FromResult<AlertThresholdsDto?>(null);

        public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) => Task.FromResult(CompanyId);

        /// <summary>Statut du tenant courant : null = pas de profil = ACTIF (defaut neutre des tests).</summary>
        public Task<string?> GetCurrentTenantStatut(CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class StubTvaMappingQueries : ITvaMappingQueries
    {
        public MappingTableDto? Table { get; init; }

        public Task<MappingTableDto?> GetMappingTable(Guid companyId, CancellationToken ct = default) => Task.FromResult(Table);

        public Task<IReadOnlyList<MappingChangeLogEntryDto>> GetChangeLog(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MappingChangeLogEntryDto>>(Array.Empty<MappingChangeLogEntryDto>());
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public StubTenantContext(string? tenantId) => TenantId = tenantId;

        public string? TenantId { get; }

        public bool IsResolved => TenantId is not null;
    }

    private sealed class StubPaClientRegistry : IPaClientRegistry
    {
        public Dictionary<string, PaCapabilities> CapabilitiesByType { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Types enregistrés dont la résolution LÈVE (ex. clé PA non saisie) — teste la dégradation.</summary>
        public HashSet<string> UnresolvableTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<string> RegisteredTypes => CapabilitiesByType.Keys;

        public bool IsRegistered(string paType) =>
            !string.IsNullOrWhiteSpace(paType) && (CapabilitiesByType.ContainsKey(paType) || UnresolvableTypes.Contains(paType));

        public IPaClient Resolve(PaAccountDescriptor account)
        {
            if (UnresolvableTypes.Contains(account.PaType))
            {
                // Simule un plug-in réel dont Create échoue (ex. déchiffrement d'une clé PA absente).
                throw new InvalidOperationException($"Résolution impossible pour « {account.PaType} » (clé absente).");
            }

            if (!CapabilitiesByType.TryGetValue(account.PaType, out var capabilities))
            {
                throw new InvalidOperationException($"Aucun plug-in pour « {account.PaType} ».");
            }

            // IPaClient réel (plug-in factice) configuré avec les capacités attendues — pas de stub maison.
            return new FakePaClientFactory(new FakePaClientOptions { Capabilities = capabilities }).Create(account);
        }
    }
}
