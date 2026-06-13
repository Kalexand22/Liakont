namespace Liakont.Host.Clients;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.MultiTenancy;
using Liakont.Host.Security.Abstractions;
using Liakont.Modules.Ingestion.Contracts.Commands;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Implémentation de <see cref="IClientConsoleService"/> (OPS03). COMPOSITION au Host (composition
/// root) : registre des tenants (<see cref="ITenantQueries"/>, base système — PAS une lecture métier
/// cross-tenant), registre des agents (système, scopé par tenantId), et profil de CHAQUE tenant lu
/// dans SON scope via les Contracts TenantSettings (précédent FIX01a / supervision). Lecture des
/// profils SÉQUENTIELLE (N petit en V1) ; un tenant illisible reste VISIBLE (ReadFailed), jamais
/// masqué. Les actions dispatchent IN-PROCESS dans le scope du tenant CIBLE, avec parité d'audit
/// (pattern WEB09 : best-effort APRÈS le succès irréversible, clé d'agent jamais journalisée).
/// </summary>
internal sealed partial class ClientConsoleService : IClientConsoleService
{
    /// <summary>Clé de configuration de la racine des dossiers de seed (défaut : deployments/ du contenu).</summary>
    public const string SeedRootConfigKey = "TenantSeeds:RootPath";

    private const string ClientEntityType = "Client";

    private readonly ITenantQueries _tenantQueries;
    private readonly IAgentQueries _agentQueries;
    private readonly ITenantScopeFactory _scopeFactory;
    private readonly ITenantProvisioningService _provisioning;
    private readonly ITenantUserProvisioningService _userProvisioning;
    private readonly ITenantSuspensionLookup _suspensionLookup;
    private readonly IActorContextAccessor _actorContext;
    private readonly IActivityLogger _activityLogger;
    private readonly string _seedRootPath;
    private readonly ILogger<ClientConsoleService> _logger;

    public ClientConsoleService(
        ITenantQueries tenantQueries,
        IAgentQueries agentQueries,
        ITenantScopeFactory scopeFactory,
        ITenantProvisioningService provisioning,
        ITenantUserProvisioningService userProvisioning,
        ITenantSuspensionLookup suspensionLookup,
        IActorContextAccessor actorContext,
        IActivityLogger activityLogger,
        IConfiguration configuration,
        ILogger<ClientConsoleService> logger)
    {
        _tenantQueries = tenantQueries;
        _agentQueries = agentQueries;
        _scopeFactory = scopeFactory;
        _provisioning = provisioning;
        _userProvisioning = userProvisioning;
        _suspensionLookup = suspensionLookup;
        _actorContext = actorContext;
        _activityLogger = activityLogger;
        _seedRootPath = configuration[SeedRootConfigKey] ?? "deployments";
        _logger = logger;
    }

    public async Task<IReadOnlyList<ClientConsoleLine>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await _tenantQueries.ListAsync(cancellationToken).ConfigureAwait(false);
        var lines = new List<ClientConsoleLine>(tenants.Count);

        foreach (var tenant in tenants)
        {
            var agentCount = 0;
            try
            {
                var agents = await _agentQueries.ListByTenantAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
                agentCount = agents.Count(a => !a.IsRevoked);
            }
            catch (Exception ex)
            {
                LogAgentCountFailed(_logger, tenant.Id, ex);
            }

            if (!tenant.IsActive)
            {
                lines.Add(Line(tenant, ClientStatut.Desactive, siren: null, agentCount));
                continue;
            }

            try
            {
                var (statut, siren) = await ReadProfileAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
                lines.Add(Line(tenant, statut, siren, agentCount));
            }
            catch (Exception ex)
            {
                // La ligne reste VISIBLE avec son échec signalé — jamais masquée, jamais silencieux.
                LogProfileReadFailed(_logger, tenant.Id, ex);
                lines.Add(Line(tenant, ClientStatut.ProfilNonCree, siren: null, agentCount) with { ReadFailed = true });
            }
        }

        return lines;
    }

    public IReadOnlyList<string> ListSeedDirectories()
    {
        var root = Path.GetFullPath(_seedRootPath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(root)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    public async Task<ClientCreationResult> CreateTenantAsync(
        string tenantId, string displayName, string adminEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(adminEmail))
        {
            return new ClientCreationResult(
                ClientActionStatus.ValidationFailed,
                "L'identifiant, la raison sociale et l'email de contact sont obligatoires.");
        }

        TenantProvisionResult result;
        try
        {
            result = await _provisioning.ProvisionAsync(
                new TenantProvisionRequest { TenantId = tenantId.Trim(), DisplayName = displayName.Trim(), AdminEmail = adminEmail.Trim() },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCreateFailed(_logger, tenantId, ex);
            return new ClientCreationResult(
                ClientActionStatus.Failed,
                "La création du client a échoué — consultez les journaux serveur puis réessayez.");
        }

        if (!result.Success)
        {
            return new ClientCreationResult(ClientActionStatus.Failed, result.ErrorMessage);
        }

        // Idempotent : un tenant déjà provisionné est une REPRISE (l'assistant continue), pas une erreur.
        var creationDescription = result.AlreadyProvisioned
            ? string.Create(CultureInfo.InvariantCulture, $"Client « {displayName} » ({tenantId}) : création rejouée (déjà provisionné).")
            : string.Create(CultureInfo.InvariantCulture, $"Client « {displayName} » ({tenantId}) créé par l'opérateur.");
        await AuditBestEffortAsync(
            tenantId,
            "clients.created",
            creationDescription,
            new { tenantId, displayName },
            cancellationToken).ConfigureAwait(false);

        return new ClientCreationResult(
            ClientActionStatus.Succeeded,
            AlreadyProvisioned: result.AlreadyProvisioned);
    }

    public async Task<ClientSeedResult> ImportSeedAsync(
        string tenantId, string seedDirectoryName, CancellationToken cancellationToken = default)
    {
        // Chemin VERROUILLÉ sous la racine configurée : le nom vient d'une liste serveur, mais on
        // re-valide (jamais de traversée de disque pilotée par l'UI).
        var root = Path.GetFullPath(_seedRootPath);
        var seedPath = Path.GetFullPath(Path.Combine(root, seedDirectoryName));
        if (!seedPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !Directory.Exists(seedPath))
        {
            return new ClientSeedResult(
                ClientActionStatus.ValidationFailed,
                $"Dossier de seed introuvable sous « {_seedRootPath} » : choisissez un dossier proposé.");
        }

        var tenant = await _tenantQueries.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return new ClientSeedResult(ClientActionStatus.NotFound, $"Tenant « {tenantId} » introuvable.");
        }

        if (tenant.CompanyId is not { } companyId)
        {
            return new ClientSeedResult(
                ClientActionStatus.Failed,
                "Ce tenant n'a pas de company_id au registre (provisionné avant son introduction) — importez son seed via l'API d'administration.");
        }

        try
        {
            await using var scope = _scopeFactory.Create(tenantId);
            var sender = scope.Services.GetRequiredService<ISender>();
            var imported = await sender.Send(
                new ImportTenantSeedCommand { SeedDirectoryPath = seedPath, CompanyId = companyId },
                cancellationToken).ConfigureAwait(false);

            await AuditBestEffortAsync(
                tenantId,
                "clients.seed_imported",
                string.Create(CultureInfo.InvariantCulture, $"Seed « {seedDirectoryName} » importé dans le client « {tenantId} »."),
                new { tenantId, seedDirectoryName },
                cancellationToken).ConfigureAwait(false);

            return new ClientSeedResult(ClientActionStatus.Succeeded, Imported: imported);
        }
        catch (ConflictException ex)
        {
            // Refus métier attendu (tenant déjà paramétré, SIREN divergent…) : message du domaine tel quel.
            return new ClientSeedResult(ClientActionStatus.Conflict, ex.Message);
        }
        catch (Exception ex)
        {
            LogSeedFailed(_logger, tenantId, ex);
            return new ClientSeedResult(
                ClientActionStatus.Failed,
                "L'import du seed a échoué — corrigez le dossier de seed puis réessayez cette étape.");
        }
    }

    public async Task<ClientActionResult> SaveProfileAsync(
        string tenantId, ClientProfileInput profile, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantQueries.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return new ClientActionResult(ClientActionStatus.NotFound, $"Tenant « {tenantId} » introuvable.");
        }

        try
        {
            await using var scope = _scopeFactory.Create(tenantId);
            var sender = scope.Services.GetRequiredService<ISender>();
            await sender.Send(
                new SaveTenantProfileCommand
                {
                    // companyId du registre (fixé au provisioning = claim du realm) ; un tenant antérieur
                    // au registre porteur passe par l'import de seed admin (chemin API).
                    CompanyId = tenant.CompanyId,
                    Siren = profile.Siren,
                    RaisonSociale = profile.RaisonSociale,
                    Street = profile.Street,
                    PostalCode = profile.PostalCode,
                    City = profile.City,
                    Country = profile.Country,
                    ContactEmailAlerte = string.IsNullOrWhiteSpace(profile.ContactEmailAlerte) ? null : profile.ContactEmailAlerte,
                },
                cancellationToken).ConfigureAwait(false);

            await AuditBestEffortAsync(
                tenantId,
                "clients.profile_saved",
                string.Create(CultureInfo.InvariantCulture, $"Profil du client « {tenantId} » créé (SIREN {profile.Siren})."),
                new { tenantId, profile.Siren },
                cancellationToken).ConfigureAwait(false);

            return new ClientActionResult(ClientActionStatus.Succeeded);
        }
        catch (ArgumentException ex)
        {
            // Validation du DOMAINE (SIREN Luhn, raison sociale, adresse) : message porté tel quel.
            return new ClientActionResult(ClientActionStatus.ValidationFailed, ex.Message);
        }
        catch (ConflictException ex)
        {
            return new ClientActionResult(ClientActionStatus.Conflict, ex.Message);
        }
        catch (Exception ex)
        {
            LogProfileSaveFailed(_logger, tenantId, ex);
            return new ClientActionResult(
                ClientActionStatus.Failed,
                "L'enregistrement du profil a échoué — réessayez cette étape.");
        }
    }

    public Task<TenantUserProvisionResult> ProvisionFirstUserAsync(
        TenantUserProvisionRequest request, CancellationToken cancellationToken = default) =>
        _userProvisioning.ProvisionUserAsync(request, cancellationToken);

    public async Task<ClientAgentKeyResult> RegisterFirstAgentAsync(
        string tenantId, string agentName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            return new ClientAgentKeyResult(ClientActionStatus.ValidationFailed, Message: "Le nom de l'agent est obligatoire.");
        }

        AgentKeyIssuedDto issued;
        try
        {
            // Dans le scope du tenant CIBLE : le handler PIV05 résout le tenant de l'ITenantContext
            // ambiant — ici celui du scope (jamais un paramètre client).
            await using var scope = _scopeFactory.Create(tenantId);
            var sender = scope.Services.GetRequiredService<ISender>();
            issued = await sender.Send(new RegisterAgentCommand { Name = agentName }, cancellationToken).ConfigureAwait(false);
        }
        catch (ConflictException ex)
        {
            return new ClientAgentKeyResult(ClientActionStatus.Conflict, Message: ex.Message);
        }
        catch (Exception ex)
        {
            LogAgentRegisterFailed(_logger, tenantId, ex);
            return new ClientAgentKeyResult(
                ClientActionStatus.Failed,
                Message: "L'enregistrement de l'agent a échoué — réessayez cette étape (ou plus tard via l'écran Agents du client).");
        }

        // La clé est émise (IRRÉVERSIBLE, affichée une fois) : TOUJOURS retournée ; l'audit best-effort
        // ne journalise que le PRÉFIXE (parité WEB09 — jamais la clé).
        await AuditBestEffortAsync(
            tenantId,
            "clients.agent_registered",
            string.Create(
                CultureInfo.InvariantCulture,
                $"Premier agent « {agentName} » enregistré pour le client « {tenantId} » (préfixe de clé {issued.KeyPrefix})."),
            new { tenantId, agentName, issued.KeyPrefix },
            cancellationToken).ConfigureAwait(false);

        return new ClientAgentKeyResult(ClientActionStatus.Succeeded, issued);
    }

    public async Task<ClientActionResult> SetStatusAsync(
        string tenantId, bool suspendre, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopeFactory.Create(tenantId);

            // Société RÉELLE du profil du tenant cible (la commande update par companyId) —
            // couvre aussi les tenants antérieurs au registre porteur.
            var settingsQueries = scope.Services.GetRequiredService<ITenantSettingsQueries>();
            var companyId = await settingsQueries.GetCurrentCompanyId(cancellationToken).ConfigureAwait(false);
            if (companyId is null)
            {
                return new ClientActionResult(
                    ClientActionStatus.NotFound,
                    "Ce client n'a pas encore de profil : importez son seed (ou créez son profil) avant de changer son statut.");
            }

            var sender = scope.Services.GetRequiredService<ISender>();
            await sender.Send(
                new SetTenantStatusCommand { Statut = suspendre ? "Suspendu" : "Actif", CompanyId = companyId },
                cancellationToken).ConfigureAwait(false);
        }
        catch (NotFoundException)
        {
            return new ClientActionResult(ClientActionStatus.NotFound, $"Profil du client « {tenantId} » introuvable.");
        }
        catch (Exception ex)
        {
            LogSetStatusFailed(_logger, tenantId, ex);
            return new ClientActionResult(
                ClientActionStatus.Failed,
                "Le changement de statut a échoué — réessayez plus tard.");
        }

        // Effet IMMÉDIAT aux frontières (lot B) : le cache du lookup de suspension est invalidé.
        _suspensionLookup.Invalidate(tenantId);

        var statusDescription = suspendre
            ? string.Create(CultureInfo.InvariantCulture, $"Client « {tenantId} » SUSPENDU par l'opérateur (push agent et connexions refusés ; données intactes).")
            : string.Create(CultureInfo.InvariantCulture, $"Client « {tenantId} » réactivé par l'opérateur.");
        await AuditBestEffortAsync(
            tenantId,
            suspendre ? "clients.suspended" : "clients.reactivated",
            statusDescription,
            new { tenantId, suspendre },
            cancellationToken).ConfigureAwait(false);

        return new ClientActionResult(ClientActionStatus.Succeeded);
    }

    private static ClientConsoleLine Line(TenantDto tenant, ClientStatut statut, string? siren, int agentCount) => new()
    {
        TenantId = tenant.Id,
        DisplayName = tenant.DisplayName,
        Siren = siren,
        Statut = statut,
        AgentCount = agentCount,
        ProvisionedAt = tenant.ProvisionedAt,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Comptage des agents impossible pour le tenant '{TenantId}' (affiché à 0).")]
    private static partial void LogAgentCountFailed(ILogger logger, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lecture du profil du tenant '{TenantId}' impossible — ligne marquée « données indisponibles ».")]
    private static partial void LogProfileReadFailed(ILogger logger, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Création du client '{TenantId}' échouée depuis la console.")]
    private static partial void LogCreateFailed(ILogger logger, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Import du seed échoué pour le client '{TenantId}' depuis la console.")]
    private static partial void LogSeedFailed(ILogger logger, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Enregistrement du profil échoué pour le client '{TenantId}' depuis la console.")]
    private static partial void LogProfileSaveFailed(ILogger logger, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Enregistrement du premier agent échoué pour le client '{TenantId}' depuis la console.")]
    private static partial void LogAgentRegisterFailed(ILogger logger, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Changement de statut échoué pour le client '{TenantId}' depuis la console.")]
    private static partial void LogSetStatusFailed(ILogger logger, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit d'action client impossible (l'action elle-même a déjà réussi).")]
    private static partial void LogAuditFailed(ILogger logger, Exception exception);

    /// <summary>Lit (statut, SIREN) du profil du tenant dans SON scope ; sans profil = (ProfilNonCree, null).</summary>
    private async Task<(ClientStatut Statut, string? Siren)> ReadProfileAsync(string tenantId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.Create(tenantId);
        var queries = scope.Services.GetRequiredService<ITenantSettingsQueries>();

        var companyId = await queries.GetCurrentCompanyId(ct).ConfigureAwait(false);
        if (companyId is not { } id)
        {
            return (ClientStatut.ProfilNonCree, null);
        }

        var profile = await queries.GetTenantProfile(id, ct).ConfigureAwait(false);
        if (profile is null)
        {
            return (ClientStatut.ProfilNonCree, null);
        }

        var statut = string.Equals(profile.Statut, "Suspendu", StringComparison.Ordinal)
            ? ClientStatut.Suspendu
            : ClientStatut.Actif;
        return (statut, profile.Siren);
    }

    /// <summary>
    /// Journalise l'action opérateur en BEST-EFFORT (parité WEB09) : l'action a déjà commité — un raté
    /// d'audit isolé est tracé puis avalé, jamais converti en échec (ni perte de clé unique).
    /// </summary>
    private async Task AuditBestEffortAsync(
        string entityId, string activityType, string description, object? metadata, CancellationToken cancellationToken)
    {
        try
        {
            var actor = _actorContext.Current;
            await _activityLogger.LogActivityAsync(
                ClientEntityType,
                entityId,
                activityType,
                description,
                actor.IsAuthenticated ? actor.UserId.ToString() : "system",
                metadata: metadata,
                companyId: actor.CompanyId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogAuditFailed(_logger, ex);
        }
    }
}
