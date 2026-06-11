namespace Liakont.Host.PaAccounts;

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Security;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Implémentation de <see cref="IPaPublicationConsoleService"/> (FIX201, décision E1). Orchestre, in-process
/// depuis le circuit serveur de la console : lecture du profil tenant (SIREN) et du compte PA actif
/// (queries TenantSettings, tenant-scopées — CLAUDE.md n°9/17), résolution du plug-in via
/// <see cref="IPaClientRegistry"/> (jamais un PA concret en dur — CLAUDE.md n°6/16), appel idempotent
/// <see cref="IPaClient.EnsureTaxReportSettingAsync"/>, et trace de l'opération (audit append-only). AUCUNE
/// valeur fiscale inventée : le réglage vient du profil (SIREN → <c>cin_scheme « 0002 »</c>) et de la saisie
/// opérateur (CLAUDE.md n°2/7). Lecture d'état DÉFENSIVE (résoudre un client vivant peut lever si la clé est
/// absente / la PA injoignable — précédent API01c) : l'écran reste utilisable.
/// </summary>
internal sealed partial class PaPublicationConsoleService : IPaPublicationConsoleService
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private readonly IActorContextAccessor _actorAccessor;
    private readonly ITenantSettingsQueries _settings;
    private readonly IPaClientRegistry _registry;
    private readonly IActivityLogger _activityLogger;
    private readonly IPermissionService _permissions;
    private readonly ILogger<PaPublicationConsoleService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Constructeur de PRODUCTION (résolu par le conteneur) — horloge système (idiome du repo).</summary>
    public PaPublicationConsoleService(
        IActorContextAccessor actorAccessor,
        ITenantSettingsQueries settings,
        IPaClientRegistry registry,
        IActivityLogger activityLogger,
        IPermissionService permissions,
        ILogger<PaPublicationConsoleService> logger)
        : this(actorAccessor, settings, registry, activityLogger, permissions, logger, TimeProvider.System)
    {
    }

    /// <summary>Constructeur testable : horloge injectée (« actif depuis » / date future).</summary>
    internal PaPublicationConsoleService(
        IActorContextAccessor actorAccessor,
        ITenantSettingsQueries settings,
        IPaClientRegistry registry,
        IActivityLogger activityLogger,
        IPermissionService permissions,
        ILogger<PaPublicationConsoleService> logger,
        TimeProvider timeProvider)
    {
        _actorAccessor = actorAccessor;
        _settings = settings;
        _registry = registry;
        _activityLogger = activityLogger;
        _permissions = permissions;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<PaPublicationState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var actor = _actorAccessor.Current;
        if (actor.CompanyId is not { } companyId || string.IsNullOrWhiteSpace(actor.TenantId))
        {
            // Profil tenant pas encore créé / tenant non résolu : rien à publier.
            return new PaPublicationState { HasActiveAccount = false, StateAvailable = false };
        }

        var profile = await _settings.GetTenantProfile(companyId, cancellationToken).ConfigureAwait(false);
        var siren = string.IsNullOrWhiteSpace(profile?.Siren) ? null : profile!.Siren;

        var active = await ResolveActiveAccountAsync(_settings, companyId, cancellationToken).ConfigureAwait(false);
        if (active is null)
        {
            return new PaPublicationState { HasActiveAccount = false, Siren = siren, StateAvailable = false };
        }

        // Lecture DÉFENSIVE de l'état côté PA : un plug-in non câblé ou une clé absente ne doit pas faire
        // échouer l'écran (précédent API01c). On dégrade en « état indisponible ».
        if (!_registry.IsRegistered(active.PluginType))
        {
            return new PaPublicationState
            {
                HasActiveAccount = true,
                PluginType = active.PluginType,
                Environment = active.Environment,
                Siren = siren,
                StateAvailable = false,
            };
        }

        try
        {
            var client = _registry.Resolve(new PaAccountDescriptor(active.PluginType, actor.TenantId!));
            var setting = await client.GetTaxReportSettingAsync(cancellationToken).ConfigureAwait(false);
            return new PaPublicationState
            {
                HasActiveAccount = true,
                PluginType = active.PluginType,
                Environment = active.Environment,
                Siren = siren,
                StateAvailable = true,

                // « Publié » = ACTIF MAINTENANT, via la SOURCE UNIQUE de la règle d'activation
                // (PaTaxReportSetting.IsActiveOn) — la MÊME que le gating d'envoi (SendTenantJob), donc
                // l'état affiché ne peut pas diverger du refus d'envoi. Une date FUTURE n'est PAS « publié »
                // (l'envoi reste refusé) : la vue l'affiche comme « programmé ».
                IsPublished = setting.IsActiveOn(DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime)),
                StartDate = setting.StartDate,
            };
        }
        catch (Exception ex)
        {
            LogStateReadFailed(_logger, active.PluginType, ex);
            return new PaPublicationState
            {
                HasActiveAccount = true,
                PluginType = active.PluginType,
                Environment = active.Environment,
                Siren = siren,
                StateAvailable = false,
            };
        }
    }

    public async Task<PaPublicationResult> PublishAsync(PaPublicationFormModel form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);

        // Défense en profondeur : la publication gère le paramétrage de transmission (garde liakont.settings,
        // comme la gestion des comptes PA) — le chemin in-process ne dépend pas du seul masquage des boutons.
        if (!_permissions.HasPermission(LiakontPermissions.Settings))
        {
            return PaPublicationResult.Failure(
                "Action non autorisée : la publication du SIREN requiert l'autorisation de paramétrage (liakont.settings).");
        }

        var actor = _actorAccessor.Current;
        if (actor.CompanyId is not { } companyId || string.IsNullOrWhiteSpace(actor.TenantId))
        {
            return PaPublicationResult.Failure("Tenant non résolu : publication impossible.");
        }

        var profile = await _settings.GetTenantProfile(companyId, cancellationToken).ConfigureAwait(false);
        if (profile is null || string.IsNullOrWhiteSpace(profile.Siren))
        {
            return PaPublicationResult.Failure(
                "Profil tenant incomplet : renseignez le SIREN (Paramétrage) avant de publier la transmission.");
        }

        if (string.IsNullOrWhiteSpace(form.TypeOperation) || string.IsNullOrWhiteSpace(form.EnterpriseSize))
        {
            return PaPublicationResult.Failure(
                "Renseignez le type d'opération et la taille d'entreprise attendus par votre Plateforme Agréée.");
        }

        var active = await ResolveActiveAccountAsync(_settings, companyId, cancellationToken).ConfigureAwait(false);
        if (active is null)
        {
            return PaPublicationResult.Failure(
                "Aucun compte Plateforme Agréée actif : configurez et activez un compte (Comptes plateforme agréée) avant de publier.");
        }

        if (!_registry.IsRegistered(active.PluginType))
        {
            return PaPublicationResult.Failure(string.Create(Fr, $"Le plug-in « {active.PluginType} » n'est pas câblé sur cette instance : publication impossible. Câblez le plug-in (déploiement) avant de publier."));
        }

        var request = TaxReportSettingRequestBuilder.Build(
            form.StartDate, form.TypeOperation.Trim(), form.EnterpriseSize.Trim(), form.NafCode);

        try
        {
            var client = _registry.Resolve(new PaAccountDescriptor(active.PluginType, actor.TenantId!));
            await client.EnsureTaxReportSettingAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // L'échec reste VISIBLE et tracé (jamais avalé) — message opérateur sans détail technique ni secret.
            LogPublishFailed(_logger, active.PluginType, ex);
            return PaPublicationResult.Failure(
                "La publication auprès de la Plateforme Agréée a échoué (vérifiez la clé API du compte et la disponibilité de la PA). Réessayez plus tard.");
        }

        // Trace append-only de l'opération (décision E1 / CLAUDE.md n°4). Aucun secret (il n'y en a pas).
        await _activityLogger.LogActivityAsync(
            PaPublicationAudit.EntityType,
            active.Id.ToString(),
            PaPublicationAudit.PublishedActivity,
            string.Create(Fr, $"Publication du SIREN / activation de la transmission auprès de « {active.PluginType} » (date de début {form.StartDate:dd/MM/yyyy})."),
            ActorId(actor),
            metadata: new
            {
                active.PluginType,
                active.Environment,
                startDate = form.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                cinScheme = TaxReportSettingRequestBuilder.SirenCinScheme,
                form.TypeOperation,
                form.EnterpriseSize,
            },
            companyId: companyId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // F05 §2 : une date de début FUTURE = SIREN pas encore publié (aucun envoi avant cette date).
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        return form.StartDate <= today
            ? PaPublicationResult.Ok(string.Create(Fr, $"SIREN publié : la transmission est active depuis le {form.StartDate:dd/MM/yyyy}. Vous pouvez maintenant lancer un envoi."))
            : PaPublicationResult.Ok(string.Create(Fr, $"Réglage enregistré : la transmission sera active le {form.StartDate:dd/MM/yyyy} (date future — aucun envoi possible avant)."));
    }

    /// <summary>Premier compte Plateforme Agréée ACTIF du tenant (la cible de l'envoi — même règle que SendTenantJob), ou <c>null</c>.</summary>
    private static async Task<PaAccountDto?> ResolveActiveAccountAsync(
        ITenantSettingsQueries settings, Guid companyId, CancellationToken cancellationToken)
    {
        var accounts = await settings.GetPaAccounts(companyId, cancellationToken).ConfigureAwait(false);
        return accounts.FirstOrDefault(static a => a.IsActive);
    }

    /// <summary>Identité d'audit de l'opérateur (GUID utilisateur ; « system » si non authentifié) — même convention que les autres actions console.</summary>
    private static string ActorId(IActorContext actor) =>
        actor.IsAuthenticated ? actor.UserId.ToString() : "system";

    [LoggerMessage(EventId = 7240, Level = LogLevel.Warning,
        Message = "Publication PA : lecture de l'état du réglage impossible pour le plug-in « {PluginType} » — état affiché indisponible (dégradé).")]
    private static partial void LogStateReadFailed(ILogger logger, string pluginType, Exception exception);

    [LoggerMessage(EventId = 7241, Level = LogLevel.Error,
        Message = "Publication PA : l'appel EnsureTaxReportSettingAsync a échoué pour le plug-in « {PluginType} » — publication refusée (re-tentable).")]
    private static partial void LogPublishFailed(ILogger logger, string pluginType, Exception exception);
}
