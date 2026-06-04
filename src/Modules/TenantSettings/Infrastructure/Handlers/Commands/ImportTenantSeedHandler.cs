namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Liakont.Modules.TenantSettings.Domain.ValueObjects;
using Liakont.Modules.TenantSettings.Infrastructure.Seed;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Importe (idempotent) le seed d'un dossier <c>deployments/&lt;client&gt;/</c> dans le tenant courant
/// (F12-A §8). Atomique (une seule transaction). N'écrit JAMAIS une clé API : chaque compte PA est
/// créé/mis à jour sans secret, avec un avertissement de complétion via la console (F12-A §8.2).
/// </summary>
public sealed class ImportTenantSeedHandler : IRequestHandler<ImportTenantSeedCommand, ImportTenantSeedResult>
{
    private readonly ITenantSettingsUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly TenantSettingsJournal _journal;

    public ImportTenantSeedHandler(
        ITenantSettingsUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        TenantSettingsJournal journal)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _journal = journal;
    }

    public async Task<ImportTenantSeedResult> Handle(ImportTenantSeedCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        var (profileSeed, paAccountSeeds) = await TenantSeedReader.ReadAsync(request.SeedDirectoryPath, cancellationToken);

        var warnings = new List<string>();
        var profileImported = false;
        var fiscalImported = false;
        var scheduleImported = false;
        var thresholdsImported = false;

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        if (profileSeed is not null)
        {
            await ImportProfileAsync(uow, companyId, profileSeed, cancellationToken);
            profileImported = true;

            if (profileSeed.Fiscal is not null)
            {
                await ImportFiscalAsync(uow, companyId, profileSeed.Fiscal, cancellationToken);
                fiscalImported = true;
            }

            if (profileSeed.Schedule is not null)
            {
                await ImportScheduleAsync(uow, companyId, profileSeed.Schedule, cancellationToken);
                scheduleImported = true;
            }

            if (profileSeed.Thresholds is not null)
            {
                await ImportThresholdsAsync(uow, companyId, profileSeed.Thresholds, cancellationToken);
                thresholdsImported = true;
            }
        }

        var paImported = await ImportPaAccountsAsync(uow, companyId, paAccountSeeds, warnings, cancellationToken);

        await uow.CommitAsync(cancellationToken);

        await _journal.RecordAsync(
            "TenantSeed",
            companyId,
            "imported",
            $"Import de seed depuis « {request.SeedDirectoryPath} ».",
            companyId,
            new { profileImported, fiscalImported, scheduleImported, thresholdsImported, paImported },
            cancellationToken);

        return new ImportTenantSeedResult
        {
            ProfileImported = profileImported,
            FiscalImported = fiscalImported,
            PaAccountsImported = paImported,
            ScheduleImported = scheduleImported,
            ThresholdsImported = thresholdsImported,
            Warnings = warnings,
        };
    }

    private static async Task ImportProfileAsync(
        ITenantSettingsUnitOfWork uow,
        Guid companyId,
        TenantProfileSeed seed,
        CancellationToken ct)
    {
        if (seed.Address is null
            || string.IsNullOrWhiteSpace(seed.Siren)
            || string.IsNullOrWhiteSpace(seed.RaisonSociale))
        {
            throw new ConflictException(
                $"Seed « {TenantSeedReader.ProfileFileName} » incomplet : siren, raisonSociale et address sont requis.");
        }

        var address = TenantAddress.Create(
            seed.Address.Street ?? string.Empty,
            seed.Address.PostalCode ?? string.Empty,
            seed.Address.City ?? string.Empty,
            seed.Address.Country ?? string.Empty);

        var existing = await uow.GetTenantProfileByCompanyAsync(companyId, ct);
        if (existing is null)
        {
            var profile = TenantProfile.Create(companyId, seed.Siren, seed.RaisonSociale, address, seed.ContactEmailAlerte);
            await uow.InsertTenantProfileAsync(profile, ct);
            return;
        }

        if (!string.Equals(existing.Siren, seed.Siren.Trim(), StringComparison.Ordinal))
        {
            throw new ConflictException(
                "INV-TENANTSETTINGS-001 : le SIREN du seed diffère du SIREN du tenant existant (clé fonctionnelle immuable).");
        }

        existing.UpdateDetails(seed.RaisonSociale, address, seed.ContactEmailAlerte);
        await uow.UpdateTenantProfileAsync(existing, ct);
    }

    private static async Task ImportFiscalAsync(
        ITenantSettingsUnitOfWork uow,
        Guid companyId,
        FiscalSeed seed,
        CancellationToken ct)
    {
        var operationCategory = TenantSettingsParsing.ParseOperationCategory(seed.OperationCategory);

        var existing = await uow.GetFiscalSettingsByCompanyAsync(companyId, ct);
        if (existing is null)
        {
            var settings = FiscalSettings.Create(companyId, seed.VatOnDebits, operationCategory, seed.ReportingFrequency);
            await uow.InsertFiscalSettingsAsync(settings, ct);
            return;
        }

        existing.Update(seed.VatOnDebits, operationCategory, seed.ReportingFrequency);
        await uow.UpdateFiscalSettingsAsync(existing, ct);
    }

    private static async Task ImportScheduleAsync(
        ITenantSettingsUnitOfWork uow,
        Guid companyId,
        ScheduleSeed seed,
        CancellationToken ct)
    {
        var hours = seed.Hours ?? [];

        var existing = await uow.GetExtractionScheduleByCompanyAsync(companyId, ct);
        if (existing is null)
        {
            var schedule = ExtractionSchedule.Create(companyId, hours, seed.CatchUpOnStart);
            await uow.InsertExtractionScheduleAsync(schedule, ct);
            return;
        }

        existing.Update(hours, seed.CatchUpOnStart);
        await uow.UpdateExtractionScheduleAsync(existing, ct);
    }

    private static async Task ImportThresholdsAsync(
        ITenantSettingsUnitOfWork uow,
        Guid companyId,
        ThresholdsSeed seed,
        CancellationToken ct)
    {
        var agentSilentHours = seed.AgentSilentHours ?? AlertThresholds.DefaultAgentSilentHours;
        var missedRunHours = seed.MissedRunHours ?? AlertThresholds.DefaultMissedRunHours;
        var pushQueueMaxItems = seed.PushQueueMaxItems ?? AlertThresholds.DefaultPushQueueMaxItems;
        var pushQueueMaxAgeHours = seed.PushQueueMaxAgeHours ?? AlertThresholds.DefaultPushQueueMaxAgeHours;
        var blockedDocumentsDays = seed.BlockedDocumentsDays ?? AlertThresholds.DefaultBlockedDocumentsDays;
        var paRejectionsDays = seed.PaRejectionsDays ?? AlertThresholds.DefaultPaRejectionsDays;

        var existing = await uow.GetAlertThresholdsByCompanyAsync(companyId, ct);
        if (existing is null)
        {
            var thresholds = AlertThresholds.Create(
                companyId,
                agentSilentHours,
                missedRunHours,
                pushQueueMaxItems,
                pushQueueMaxAgeHours,
                blockedDocumentsDays,
                paRejectionsDays,
                seed.AlertTenantContact);
            await uow.InsertAlertThresholdsAsync(thresholds, ct);
            return;
        }

        existing.Update(
            agentSilentHours,
            missedRunHours,
            pushQueueMaxItems,
            pushQueueMaxAgeHours,
            blockedDocumentsDays,
            paRejectionsDays,
            seed.AlertTenantContact);
        await uow.UpdateAlertThresholdsAsync(existing, ct);
    }

    private static async Task<int> ImportPaAccountsAsync(
        ITenantSettingsUnitOfWork uow,
        Guid companyId,
        IReadOnlyList<PaAccountSeed> seeds,
        List<string> warnings,
        CancellationToken ct)
    {
        if (seeds.Count == 0)
        {
            return 0;
        }

        var existing = await uow.GetPaAccountsByCompanyAsync(companyId, ct);
        var imported = 0;

        foreach (var seed in seeds)
        {
            if (string.IsNullOrWhiteSpace(seed.PluginType) || string.IsNullOrWhiteSpace(seed.Environment))
            {
                throw new ConflictException(
                    $"Seed « {TenantSeedReader.PaAccountsFileName} » : pluginType et environment sont requis pour chaque compte PA.");
            }

            var environment = TenantSettingsParsing.ParseEnvironment(seed.Environment);
            var identifiers = seed.AccountIdentifiers ?? string.Empty;

            var match = existing.FirstOrDefault(a =>
                string.Equals(a.PluginType, seed.PluginType.Trim(), StringComparison.Ordinal)
                && a.Environment == environment);

            if (match is null)
            {
                // Aucune clé importée : encryptedApiKey null (placeholder à compléter via la console).
                var account = PaAccount.Create(companyId, seed.PluginType, environment, identifiers, encryptedApiKey: null);
                await uow.InsertPaAccountAsync(account, ct);
            }
            else
            {
                match.UpdateDetails(environment, identifiers);
                await uow.UpdatePaAccountAsync(match, ct);
            }

            warnings.Add(
                $"Clé API du compte PA « {seed.PluginType.Trim()} / {environment} » non importée (placeholder — à saisir via la console).");
            imported++;
        }

        return imported;
    }
}
