namespace Liakont.Modules.TenantSettings.Infrastructure.Queries;

using Dapper;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lectures Dapper du paramétrage tenant (read-models). Toujours scopé par <c>company_id</c>
/// (CLAUDE.md n°9/17). Pour les comptes PA, la requête ne SÉLECTIONNE jamais la colonne
/// <c>encrypted_api_key</c> : seule l'existence d'une clé est exposée (INV-TENANTSETTINGS-003).
/// </summary>
public sealed class PostgresTenantSettingsQueries : ITenantSettingsQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresTenantSettingsQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, company_id, siren, raison_sociale, address_street, address_postal_code,
                   address_city, address_country, contact_email_alerte, statut, created_at, updated_at
            FROM tenantsettings.tenant_profiles
            WHERE company_id = @CompanyId
            """;

        using var conn = await _connectionFactory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        return new TenantProfileDto
        {
            Id = (Guid)row.id,
            CompanyId = (Guid)row.company_id,
            Siren = (string)row.siren,
            RaisonSociale = (string)row.raison_sociale,
            Street = (string)row.address_street,
            PostalCode = (string)row.address_postal_code,
            City = (string)row.address_city,
            Country = (string)row.address_country,
            ContactEmailAlerte = (string?)row.contact_email_alerte,
            Statut = ((TenantStatus)(int)row.statut).ToString(),
            CreatedAt = TenantSettingsRowReader.ToDateTimeOffset((object)row.created_at),
            UpdatedAt = TenantSettingsRowReader.ToNullableDateTimeOffset((object?)row.updated_at),
        };
    }

    public async Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, company_id, vat_on_debits, operation_category, reporting_frequency,
                   fee_imputation_method, created_at, updated_at
            FROM tenantsettings.fiscal_settings
            WHERE company_id = @CompanyId
            """;

        using var conn = await _connectionFactory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        int? categoryInt = (int?)row.operation_category;
        int? feeMethodInt = (int?)row.fee_imputation_method;

        return new FiscalSettingsDto
        {
            Id = (Guid)row.id,
            CompanyId = (Guid)row.company_id,
            VatOnDebits = (bool?)row.vat_on_debits,
            OperationCategory = categoryInt.HasValue ? ((OperationCategory)categoryInt.Value).ToString() : null,
            ReportingFrequency = (string?)row.reporting_frequency,
            FeeImputationMethod = feeMethodInt.HasValue ? ((FeeImputationMethod)feeMethodInt.Value).ToString() : null,
            CreatedAt = TenantSettingsRowReader.ToDateTimeOffset((object)row.created_at),
            UpdatedAt = TenantSettingsRowReader.ToNullableDateTimeOffset((object?)row.updated_at),
        };
    }

    public async Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default)
    {
        // La clé chiffrée n'est JAMAIS sélectionnée : on n'expose que son existence (has_api_key).
        const string sql = """
            SELECT id, company_id, plugin_type, environment, account_identifiers,
                   (encrypted_api_key IS NOT NULL) AS has_api_key, is_active, created_at, updated_at
            FROM tenantsettings.pa_accounts
            WHERE company_id = @CompanyId
            ORDER BY created_at ASC
            """;

        using var conn = await _connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        var result = new List<PaAccountDto>();
        foreach (var row in rows)
        {
            result.Add(new PaAccountDto
            {
                Id = (Guid)row.id,
                CompanyId = (Guid)row.company_id,
                PluginType = (string)row.plugin_type,
                Environment = ((PaEnvironment)(int)row.environment).ToString(),
                AccountIdentifiers = (string)row.account_identifiers,
                HasApiKey = (bool)row.has_api_key,
                IsActive = (bool)row.is_active,
                CreatedAt = TenantSettingsRowReader.ToDateTimeOffset((object)row.created_at),
                UpdatedAt = TenantSettingsRowReader.ToNullableDateTimeOffset((object?)row.updated_at),
            });
        }

        return result;
    }

    public async Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, company_id, hours, catch_up_on_start, created_at, updated_at
            FROM tenantsettings.extraction_schedules
            WHERE company_id = @CompanyId
            """;

        using var conn = await _connectionFactory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        return new ExtractionScheduleDto
        {
            Id = (Guid)row.id,
            CompanyId = (Guid)row.company_id,
            Hours = TenantSettingsRowReader.ToStringList((object?)row.hours),
            CatchUpOnStart = (bool)row.catch_up_on_start,
            CreatedAt = TenantSettingsRowReader.ToDateTimeOffset((object)row.created_at),
            UpdatedAt = TenantSettingsRowReader.ToNullableDateTimeOffset((object?)row.updated_at),
        };
    }

    public async Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default)
    {
        // La base est PAR TENANT et tenant_profiles porte au plus une ligne (company_id UNIQUE) : on
        // retourne le company_id de l'unique profil, ou null s'il n'est pas encore créé (CFG02). Aucune
        // donnée d'un autre tenant n'est accessible (la connexion EST la base du tenant — CLAUDE.md n°9).
        const string sql = "SELECT company_id FROM tenantsettings.tenant_profiles LIMIT 1";

        using var conn = await _connectionFactory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<string?> GetCurrentTenantStatut(CancellationToken ct = default)
    {
        // Même patron que GetCurrentCompanyId (au plus une ligne de profil par base — database-per-tenant).
        // null = aucun profil → le tenant est réputé ACTIF par l'appelant (jamais de suspension implicite).
        const string sql = "SELECT statut FROM tenantsettings.tenant_profiles LIMIT 1";

        using var conn = await _connectionFactory.OpenAsync(ct);
        var statut = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(sql, cancellationToken: ct));
        return statut is { } value ? ((TenantStatus)value).ToString() : null;
    }

    public async Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, company_id, agent_silent_hours, missed_run_hours, push_queue_max_items,
                   push_queue_max_age_hours, blocked_documents_days, pa_rejections_days,
                   alert_tenant_contact, created_at, updated_at
            FROM tenantsettings.alert_thresholds
            WHERE company_id = @CompanyId
            """;

        using var conn = await _connectionFactory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        return new AlertThresholdsDto
        {
            Id = (Guid)row.id,
            CompanyId = (Guid)row.company_id,
            AgentSilentHours = (int)row.agent_silent_hours,
            MissedRunHours = (int)row.missed_run_hours,
            PushQueueMaxItems = (int)row.push_queue_max_items,
            PushQueueMaxAgeHours = (int)row.push_queue_max_age_hours,
            BlockedDocumentsDays = (int)row.blocked_documents_days,
            PaRejectionsDays = (int)row.pa_rejections_days,
            AlertTenantContact = (bool)row.alert_tenant_contact,
            CreatedAt = TenantSettingsRowReader.ToDateTimeOffset((object)row.created_at),
            UpdatedAt = TenantSettingsRowReader.ToNullableDateTimeOffset((object?)row.updated_at),
        };
    }

    public async Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT enabled
            FROM tenantsettings.auction_vertical_settings
            WHERE company_id = @CompanyId
            """;

        using var conn = await _connectionFactory.OpenAsync(ct);
        var enabled = await conn.QuerySingleOrDefaultAsync<bool?>(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        // Ligne absente = vertical enchères DÉSACTIVÉ (défaut produit D4, jamais une activation implicite).
        return enabled ?? false;
    }
}
