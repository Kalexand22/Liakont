namespace Liakont.Modules.TenantSettings.Infrastructure;

using Dapper;
using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Liakont.Modules.TenantSettings.Domain.ValueObjects;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Unité de travail Dapper du module TenantSettings. Toutes les requêtes sont scopées par
/// <c>company_id</c> (CLAUDE.md n°9). CRUD de paramétrage uniquement — aucun chemin d'update/delete
/// sur une table d'audit (la piste d'audit append-only est gérée hors de cette UoW).
/// </summary>
internal sealed class PostgresTenantSettingsUnitOfWork : ITenantSettingsUnitOfWork
{
    private readonly TransactionScope _txn;

    private PostgresTenantSettingsUnitOfWork(TransactionScope txn)
    {
        _txn = txn;
    }

    public static async Task<PostgresTenantSettingsUnitOfWork> BeginAsync(
        IConnectionFactory connectionFactory,
        CancellationToken ct = default)
    {
        var txn = await TransactionScope.BeginAsync(connectionFactory, ct);
        return new PostgresTenantSettingsUnitOfWork(txn);
    }

    // ───────────────────────────── Profil ─────────────────────────────
    public async Task<TenantProfile?> GetTenantProfileByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, company_id, siren, raison_sociale, address_street, address_postal_code,
                   address_city, address_country, contact_email_alerte, statut, created_at, updated_at
            FROM tenantsettings.tenant_profiles
            WHERE company_id = @CompanyId
            """;

        var row = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, _txn.Transaction, cancellationToken: ct));

        return row is null ? null : MapProfile(row);
    }

    public async Task InsertTenantProfileAsync(TenantProfile profile, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO tenantsettings.tenant_profiles
                (id, company_id, siren, raison_sociale, address_street, address_postal_code,
                 address_city, address_country, contact_email_alerte, statut, created_at, updated_at)
            VALUES
                (@Id, @CompanyId, @Siren, @RaisonSociale, @Street, @PostalCode, @City, @Country,
                 @ContactEmailAlerte, @Statut, @CreatedAt, @UpdatedAt)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                profile.Id,
                profile.CompanyId,
                profile.Siren,
                profile.RaisonSociale,
                profile.Address.Street,
                profile.Address.PostalCode,
                profile.Address.City,
                profile.Address.Country,
                profile.ContactEmailAlerte,
                Statut = (int)profile.Statut,
                profile.CreatedAt,
                profile.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateTenantProfileAsync(TenantProfile profile, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE tenantsettings.tenant_profiles
            SET raison_sociale       = @RaisonSociale,
                address_street       = @Street,
                address_postal_code  = @PostalCode,
                address_city         = @City,
                address_country      = @Country,
                contact_email_alerte = @ContactEmailAlerte,
                statut               = @Statut,
                updated_at           = @UpdatedAt
            WHERE id = @Id AND company_id = @CompanyId
            """;

        var rows = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                profile.Id,
                profile.CompanyId,
                profile.RaisonSociale,
                profile.Address.Street,
                profile.Address.PostalCode,
                profile.Address.City,
                profile.Address.Country,
                profile.ContactEmailAlerte,
                Statut = (int)profile.Statut,
                profile.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));

        EnsureUpdated(rows, "TenantProfile", profile.Id);
    }

    // ──────────────────────── Paramétrage fiscal ───────────────────────
    public async Task<FiscalSettings?> GetFiscalSettingsByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, company_id, vat_on_debits, operation_category, reporting_frequency, created_at, updated_at
            FROM tenantsettings.fiscal_settings
            WHERE company_id = @CompanyId
            """;

        var row = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, _txn.Transaction, cancellationToken: ct));

        return row is null ? null : MapFiscal(row);
    }

    public async Task InsertFiscalSettingsAsync(FiscalSettings settings, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO tenantsettings.fiscal_settings
                (id, company_id, vat_on_debits, operation_category, reporting_frequency, created_at, updated_at)
            VALUES
                (@Id, @CompanyId, @VatOnDebits, @OperationCategory, @ReportingFrequency, @CreatedAt, @UpdatedAt)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                settings.Id,
                settings.CompanyId,
                settings.VatOnDebits,
                OperationCategory = ToNullableInt(settings.OperationCategory),
                settings.ReportingFrequency,
                settings.CreatedAt,
                settings.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateFiscalSettingsAsync(FiscalSettings settings, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE tenantsettings.fiscal_settings
            SET vat_on_debits       = @VatOnDebits,
                operation_category  = @OperationCategory,
                reporting_frequency = @ReportingFrequency,
                updated_at          = @UpdatedAt
            WHERE id = @Id AND company_id = @CompanyId
            """;

        var rows = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                settings.Id,
                settings.CompanyId,
                settings.VatOnDebits,
                OperationCategory = ToNullableInt(settings.OperationCategory),
                settings.ReportingFrequency,
                settings.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));

        EnsureUpdated(rows, "FiscalSettings", settings.Id);
    }

    // ───────────────────────────── Comptes PA ─────────────────────────────
    public async Task<PaAccount?> GetPaAccountByIdAsync(Guid id, Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, company_id, plugin_type, environment, account_identifiers, encrypted_api_key,
                   is_active, created_at, updated_at
            FROM tenantsettings.pa_accounts
            WHERE id = @Id AND company_id = @CompanyId
            """;

        var row = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id, CompanyId = companyId }, _txn.Transaction, cancellationToken: ct));

        return row is null ? null : MapPaAccount(row);
    }

    public async Task<IReadOnlyList<PaAccount>> GetPaAccountsByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, company_id, plugin_type, environment, account_identifiers, encrypted_api_key,
                   is_active, created_at, updated_at
            FROM tenantsettings.pa_accounts
            WHERE company_id = @CompanyId
            ORDER BY created_at ASC
            """;

        var rows = await _txn.Connection.QueryAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, _txn.Transaction, cancellationToken: ct));

        var result = new List<PaAccount>();
        foreach (var row in rows)
        {
            result.Add(MapPaAccount(row));
        }

        return result;
    }

    public async Task InsertPaAccountAsync(PaAccount account, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO tenantsettings.pa_accounts
                (id, company_id, plugin_type, environment, account_identifiers, encrypted_api_key,
                 is_active, created_at, updated_at)
            VALUES
                (@Id, @CompanyId, @PluginType, @Environment, @AccountIdentifiers, @EncryptedApiKey,
                 @IsActive, @CreatedAt, @UpdatedAt)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                account.Id,
                account.CompanyId,
                account.PluginType,
                Environment = (int)account.Environment,
                account.AccountIdentifiers,
                account.EncryptedApiKey,
                account.IsActive,
                account.CreatedAt,
                account.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdatePaAccountAsync(PaAccount account, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE tenantsettings.pa_accounts
            SET plugin_type         = @PluginType,
                environment         = @Environment,
                account_identifiers = @AccountIdentifiers,
                encrypted_api_key   = @EncryptedApiKey,
                is_active           = @IsActive,
                updated_at          = @UpdatedAt
            WHERE id = @Id AND company_id = @CompanyId
            """;

        var rows = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                account.Id,
                account.CompanyId,
                account.PluginType,
                Environment = (int)account.Environment,
                account.AccountIdentifiers,
                account.EncryptedApiKey,
                account.IsActive,
                account.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));

        EnsureUpdated(rows, "PaAccount", account.Id);
    }

    // ──────────────────── Planification d'extraction ───────────────────
    public async Task<ExtractionSchedule?> GetExtractionScheduleByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, company_id, hours, catch_up_on_start, created_at, updated_at
            FROM tenantsettings.extraction_schedules
            WHERE company_id = @CompanyId
            """;

        var row = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, _txn.Transaction, cancellationToken: ct));

        return row is null ? null : MapSchedule(row);
    }

    public async Task InsertExtractionScheduleAsync(ExtractionSchedule schedule, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO tenantsettings.extraction_schedules
                (id, company_id, hours, catch_up_on_start, created_at, updated_at)
            VALUES
                (@Id, @CompanyId, @Hours, @CatchUpOnStart, @CreatedAt, @UpdatedAt)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                schedule.Id,
                schedule.CompanyId,
                Hours = schedule.Hours.ToArray(),
                schedule.CatchUpOnStart,
                schedule.CreatedAt,
                schedule.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateExtractionScheduleAsync(ExtractionSchedule schedule, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE tenantsettings.extraction_schedules
            SET hours             = @Hours,
                catch_up_on_start = @CatchUpOnStart,
                updated_at        = @UpdatedAt
            WHERE id = @Id AND company_id = @CompanyId
            """;

        var rows = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                schedule.Id,
                schedule.CompanyId,
                Hours = schedule.Hours.ToArray(),
                schedule.CatchUpOnStart,
                schedule.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));

        EnsureUpdated(rows, "ExtractionSchedule", schedule.Id);
    }

    // ─────────────────────────── Seuils d'alerte ───────────────────────────
    public async Task<AlertThresholds?> GetAlertThresholdsByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, company_id, agent_silent_hours, missed_run_hours, push_queue_max_items,
                   push_queue_max_age_hours, blocked_documents_days, pa_rejections_days,
                   alert_tenant_contact, created_at, updated_at
            FROM tenantsettings.alert_thresholds
            WHERE company_id = @CompanyId
            """;

        var row = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, _txn.Transaction, cancellationToken: ct));

        return row is null ? null : MapThresholds(row);
    }

    public async Task InsertAlertThresholdsAsync(AlertThresholds thresholds, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO tenantsettings.alert_thresholds
                (id, company_id, agent_silent_hours, missed_run_hours, push_queue_max_items,
                 push_queue_max_age_hours, blocked_documents_days, pa_rejections_days,
                 alert_tenant_contact, created_at, updated_at)
            VALUES
                (@Id, @CompanyId, @AgentSilentHours, @MissedRunHours, @PushQueueMaxItems,
                 @PushQueueMaxAgeHours, @BlockedDocumentsDays, @PaRejectionsDays,
                 @AlertTenantContact, @CreatedAt, @UpdatedAt)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                thresholds.Id,
                thresholds.CompanyId,
                thresholds.AgentSilentHours,
                thresholds.MissedRunHours,
                thresholds.PushQueueMaxItems,
                thresholds.PushQueueMaxAgeHours,
                thresholds.BlockedDocumentsDays,
                thresholds.PaRejectionsDays,
                thresholds.AlertTenantContact,
                thresholds.CreatedAt,
                thresholds.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateAlertThresholdsAsync(AlertThresholds thresholds, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE tenantsettings.alert_thresholds
            SET agent_silent_hours       = @AgentSilentHours,
                missed_run_hours         = @MissedRunHours,
                push_queue_max_items     = @PushQueueMaxItems,
                push_queue_max_age_hours = @PushQueueMaxAgeHours,
                blocked_documents_days   = @BlockedDocumentsDays,
                pa_rejections_days       = @PaRejectionsDays,
                alert_tenant_contact     = @AlertTenantContact,
                updated_at               = @UpdatedAt
            WHERE id = @Id AND company_id = @CompanyId
            """;

        var rows = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                thresholds.Id,
                thresholds.CompanyId,
                thresholds.AgentSilentHours,
                thresholds.MissedRunHours,
                thresholds.PushQueueMaxItems,
                thresholds.PushQueueMaxAgeHours,
                thresholds.BlockedDocumentsDays,
                thresholds.PaRejectionsDays,
                thresholds.AlertTenantContact,
                thresholds.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));

        EnsureUpdated(rows, "AlertThresholds", thresholds.Id);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _txn.CommitAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _txn.DisposeAsync();
    }

    // ───────────────────────────── Mapping ─────────────────────────────
    private static TenantProfile MapProfile(dynamic row)
    {
        var address = new TenantAddress(
            (string)row.address_street,
            (string)row.address_postal_code,
            (string)row.address_city,
            (string)row.address_country);

        return TenantProfile.Reconstitute(
            (Guid)row.id,
            (Guid)row.company_id,
            (string)row.siren,
            (string)row.raison_sociale,
            address,
            (string?)row.contact_email_alerte,
            (TenantStatus)(int)row.statut,
            TenantSettingsRowReader.ToDateTimeOffset((object)row.created_at),
            TenantSettingsRowReader.ToNullableDateTimeOffset((object?)row.updated_at));
    }

    private static FiscalSettings MapFiscal(dynamic row)
    {
        int? categoryInt = (int?)row.operation_category;

        return FiscalSettings.Reconstitute(
            (Guid)row.id,
            (Guid)row.company_id,
            (bool?)row.vat_on_debits,
            categoryInt.HasValue ? (OperationCategory)categoryInt.Value : null,
            (string?)row.reporting_frequency,
            TenantSettingsRowReader.ToDateTimeOffset((object)row.created_at),
            TenantSettingsRowReader.ToNullableDateTimeOffset((object?)row.updated_at));
    }

    private static PaAccount MapPaAccount(dynamic row)
    {
        return PaAccount.Reconstitute(
            (Guid)row.id,
            (Guid)row.company_id,
            (string)row.plugin_type,
            (PaEnvironment)(int)row.environment,
            (string)row.account_identifiers,
            (string?)row.encrypted_api_key,
            (bool)row.is_active,
            TenantSettingsRowReader.ToDateTimeOffset((object)row.created_at),
            TenantSettingsRowReader.ToNullableDateTimeOffset((object?)row.updated_at));
    }

    private static ExtractionSchedule MapSchedule(dynamic row)
    {
        return ExtractionSchedule.Reconstitute(
            (Guid)row.id,
            (Guid)row.company_id,
            TenantSettingsRowReader.ToStringList((object?)row.hours),
            (bool)row.catch_up_on_start,
            TenantSettingsRowReader.ToDateTimeOffset((object)row.created_at),
            TenantSettingsRowReader.ToNullableDateTimeOffset((object?)row.updated_at));
    }

    private static AlertThresholds MapThresholds(dynamic row)
    {
        return AlertThresholds.Reconstitute(
            (Guid)row.id,
            (Guid)row.company_id,
            (int)row.agent_silent_hours,
            (int)row.missed_run_hours,
            (int)row.push_queue_max_items,
            (int)row.push_queue_max_age_hours,
            (int)row.blocked_documents_days,
            (int)row.pa_rejections_days,
            (bool)row.alert_tenant_contact,
            TenantSettingsRowReader.ToDateTimeOffset((object)row.created_at),
            TenantSettingsRowReader.ToNullableDateTimeOffset((object?)row.updated_at));
    }

    private static int? ToNullableInt(OperationCategory? category)
    {
        return category.HasValue ? (int)category.Value : null;
    }

    private static void EnsureUpdated(int rowsAffected, string entityName, Guid id)
    {
        if (rowsAffected != 1)
        {
            throw new Stratum.Common.Abstractions.Exceptions.NotFoundException(entityName, id);
        }
    }
}
