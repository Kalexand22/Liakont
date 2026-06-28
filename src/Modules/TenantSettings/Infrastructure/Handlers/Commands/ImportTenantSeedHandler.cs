namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Liakont.Modules.TenantSettings.Infrastructure.Seed;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Importe (idempotent) le seed d'un dossier <c>deployments/&lt;client&gt;/</c> dans le tenant courant
/// (F12-A §8). Le PARAMÉTRAGE TenantSettings (fiscal, planification, seuils, comptes PA) est écrit en UNE
/// transaction. L'IDENTITÉ LÉGALE (SIREN, raison sociale, adresse, contact) n'est JAMAIS importée du seed
/// (BUG-14) : elle est saisie manuellement à la création du tenant et ne doit pas être écrasée par une
/// baseline de démo. La table de mapping TVA (item FIX01b) est importée APRÈS ce commit, dans une
/// transaction distincte (module TvaMapping) — donc PAS d'atomicité globale : si l'import de mapping
/// échoue (seed illisible/code rejeté), le paramétrage TenantSettings est déjà committé ; les deux côtés
/// étant idempotents, un ré-run récupère l'état complet. N'écrit JAMAIS une clé API (placeholders vides).
/// </summary>
public sealed class ImportTenantSeedHandler : IRequestHandler<ImportTenantSeedCommand, ImportTenantSeedResult>
{
    private readonly ITenantSettingsUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly IActorContextAccessor _actorContextAccessor;
    private readonly ITenantSettingsQueries _settingsQueries;
    private readonly TenantSettingsJournal _journal;
    private readonly ISender _sender;

    public ImportTenantSeedHandler(
        ITenantSettingsUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        IActorContextAccessor actorContextAccessor,
        ITenantSettingsQueries settingsQueries,
        TenantSettingsJournal journal,
        ISender sender)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _actorContextAccessor = actorContextAccessor;
        _settingsQueries = settingsQueries;
        _journal = journal;
        _sender = sender;
    }

    public async Task<ImportTenantSeedResult> Handle(ImportTenantSeedCommand request, CancellationToken cancellationToken)
    {
        var companyId = await TenantSettingsCompanyOverrideGuard.ResolveAsync(
            request.CompanyId, _companyFilter, _actorContextAccessor, _settingsQueries, cancellationToken);
        var (profileSeed, paAccountSeeds) = await TenantSeedReader.ReadAsync(request.SeedDirectoryPath, cancellationToken);

        var warnings = new List<string>();
        var fiscalImported = false;
        var scheduleImported = false;
        var thresholdsImported = false;

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        if (profileSeed is not null)
        {
            // BUG-14 : l'identité légale n'est JAMAIS importée du seed (saisie manuelle, jamais écrasée).
            // Le fichier tenant-profile.json ne porte plus que du paramétrage (fiscal/planif/seuils).
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
        var mappingFilePath = Path.Combine(request.SeedDirectoryPath, ImportTenantSeedCommand.MappingSeedFileName);
        if (File.Exists(mappingFilePath))
        {
            // Propage le companyId DÉJÀ résolu : au démarrage il n'y a pas de contexte de société ambiant,
            // donc le handler de mapping ne pourrait pas le re-déduire (cause du seed partiel FIX203a).
            tvaMappingImported = await _sender.Send(
                new ImportMappingTableSeedCommand { SeedFilePath = mappingFilePath, CompanyId = companyId },
                cancellationToken);
        }

        await _journal.RecordAsync(
            "TenantSeed",
            companyId,
            "imported",
            $"Import de seed depuis « {request.SeedDirectoryPath} ».",
            companyId,
            new { fiscalImported, scheduleImported, thresholdsImported, paImported, tvaMappingImported },
            cancellationToken);

        return new ImportTenantSeedResult
        {
            FiscalImported = fiscalImported,
            PaAccountsImported = paImported,
            ScheduleImported = scheduleImported,
            ThresholdsImported = thresholdsImported,
            TvaMappingImported = tvaMappingImported,
            Warnings = warnings,
        };
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
