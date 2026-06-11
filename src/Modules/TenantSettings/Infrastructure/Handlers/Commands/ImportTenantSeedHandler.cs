namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Liakont.Modules.TenantSettings.Domain.ValueObjects;
using Liakont.Modules.TenantSettings.Infrastructure.Seed;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Importe (idempotent) le seed d'un dossier <c>deployments/&lt;client&gt;/</c> dans le tenant courant
/// (F12-A §8). Le paramétrage TenantSettings (profil, fiscal, planification, seuils, comptes PA) est écrit
/// en UNE transaction. La table de mapping TVA (item FIX01b) est importée APRÈS ce commit, dans une
/// transaction distincte (module TvaMapping) — donc PAS d'atomicité globale : si l'import de mapping
/// échoue (seed illisible/code rejeté), le paramétrage TenantSettings est déjà committé ; les deux côtés
/// étant idempotents, un ré-run récupère l'état complet. N'écrit JAMAIS une clé API (placeholders vides).
/// </summary>
public sealed class ImportTenantSeedHandler : IRequestHandler<ImportTenantSeedCommand, ImportTenantSeedResult>
{
    /// <summary>Nom du fichier de seed de mapping TVA dans le dossier de seed (item FIX01b).</summary>
    private const string MappingFileName = "mapping-tva.json";

    private readonly ITenantSettingsUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly TenantSettingsJournal _journal;
    private readonly ISender _sender;

    public ImportTenantSeedHandler(
        ITenantSettingsUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        TenantSettingsJournal journal,
        ISender sender)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _journal = journal;
        _sender = sender;
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

        // Table de mapping TVA (item FIX01b) : importée par le MÊME point d'entrée OPS03, via la commande
        // TvaMapping (module séparé → dispatch MediatR par Contracts, CLAUDE.md n°6). Hors de la transaction
        // TenantSettings ci-dessus (le module TvaMapping porte sa propre transaction) ; idempotent (ignoré
        // si une table existe déjà). N'amorce que si un fichier de mapping est présent dans le dossier de seed.
        var tvaMappingImported = false;
        var mappingFilePath = Path.Combine(request.SeedDirectoryPath, MappingFileName);
        if (File.Exists(mappingFilePath))
        {
            tvaMappingImported = await _sender.Send(
                new ImportMappingTableSeedCommand { SeedFilePath = mappingFilePath },
                cancellationToken);
        }

        await _journal.RecordAsync(
            "TenantSeed",
            companyId,
            "imported",
            $"Import de seed depuis « {request.SeedDirectoryPath} ».",
            companyId,
            new { profileImported, fiscalImported, scheduleImported, thresholdsImported, paImported, tvaMappingImported },
            cancellationToken);

        return new ImportTenantSeedResult
        {
            ProfileImported = profileImported,
            FiscalImported = fiscalImported,
            PaAccountsImported = paImported,
            ScheduleImported = scheduleImported,
            ThresholdsImported = thresholdsImported,
            TvaMappingImported = tvaMappingImported,
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
        var feeImputationMethod = TenantSettingsParsing.ParseFeeImputationMethod(seed.FeeImputationMethod);

        var existing = await uow.GetFiscalSettingsByCompanyAsync(companyId, ct);
        if (existing is null)
        {
            var settings = FiscalSettings.Create(companyId, seed.VatOnDebits, operationCategory, seed.ReportingFrequency, feeImputationMethod);
            await uow.InsertFiscalSettingsAsync(settings, ct);
            return;
        }

        existing.Update(seed.VatOnDebits, operationCategory, seed.ReportingFrequency, feeImputationMethod);
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
