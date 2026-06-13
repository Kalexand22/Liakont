namespace Liakont.Agent.Installer.Configuration;

using System;
using System.Collections.Generic;
using System.Linq;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Core.Security;
using Liakont.Agent.Installer.Profiles;

/// <summary>
/// Moteur de configuration et d'installation de l'agent — le CŒUR partagé par le wizard (OPS08b §4) et
/// le mode silencieux (F13 §3 : « le wizard et le mode silencieux partagent le même moteur de
/// configuration »). Il orchestre les étapes (tests source/serveur, résolution du profil + saisie,
/// validation du nom d'instance, écriture de <c>agent.json</c> chiffré, déploiement) en s'appuyant
/// UNIQUEMENT sur des ports injectés (sondes, catalogue d'instances, déployeur, chiffrement) — d'où sa
/// testabilité « Core mocké », sans base, plateforme ni service réels. Aucune logique métier (F13 §1,
/// CLAUDE.md n°6) : il pose, configure, teste, transmet.
/// </summary>
internal sealed class InstallerEngine
{
    private readonly ISourceConnectionProbe _sourceProbe;
    private readonly IPlatformConnectionProbe _platformProbe;
    private readonly IInstalledInstanceCatalog _instanceCatalog;
    private readonly IAgentDeployer _deployer;
    private readonly ISecretProtector _protector;

    public InstallerEngine(
        ISourceConnectionProbe sourceProbe,
        IPlatformConnectionProbe platformProbe,
        IInstalledInstanceCatalog instanceCatalog,
        IAgentDeployer deployer,
        ISecretProtector protector)
    {
        _sourceProbe = sourceProbe ?? throw new ArgumentNullException(nameof(sourceProbe));
        _platformProbe = platformProbe ?? throw new ArgumentNullException(nameof(platformProbe));
        _instanceCatalog = instanceCatalog ?? throw new ArgumentNullException(nameof(instanceCatalog));
        _deployer = deployer ?? throw new ArgumentNullException(nameof(deployer));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    /// <summary>Teste la base source en lecture seule (bouton « Tester » de l'écran source).</summary>
    public SourceTestResult TestSource(string odbcConnectionString) => _sourceProbe.Test(odbcConnectionString);

    /// <summary>Teste le serveur centralisé par un heartbeat à blanc (bouton « Tester » de l'écran serveur).</summary>
    public PlatformTestResult TestPlatform(string platformUrl, string apiKey) => _platformProbe.Test(platformUrl, apiKey);

    /// <summary>Liste les instances de l'agent déjà installées sur ce poste (détection multi-instances).</summary>
    public IReadOnlyList<string> ListInstalledInstances() => _instanceCatalog.ListInstalledInstanceNames();

    /// <summary>
    /// Valide un nom d'instance NOUVELLE : il doit être un nom de service Windows valide
    /// (<see cref="AgentInstance.TryParse"/>) ET ne pas être déjà installé sur ce poste. Renseigne
    /// <paramref name="error"/> (français) et rend <c>false</c> sinon.
    /// </summary>
    public bool TryValidateNewInstanceName(string? rawName, out string instanceName, out string? error)
    {
        instanceName = string.Empty;
        if (!AgentInstance.TryParse(rawName, out AgentInstance instance, out error))
        {
            return false;
        }

        if (IsAlreadyInstalled(instance, out error))
        {
            return false;
        }

        instanceName = instance.Name;
        error = null;
        return true;
    }

    /// <summary>
    /// Installe une instance : résout le profil + la saisie, valide le nom d'instance (format + unicité),
    /// construit <c>agent.json</c> (secrets chiffrés DPAPI) et délègue le déploiement au port. Bloque (sans
    /// rien déployer) à la première erreur — jamais d'installation muette (CLAUDE.md n°3).
    /// </summary>
    public InstallationResult Install(IntegratorProfile profile, InstallationInput input)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        ResolvedConfiguration resolved = ConfigurationResolver.Resolve(profile, input, out IReadOnlyList<string> resolveErrors);
        if (resolveErrors.Count > 0)
        {
            return new InstallationResult(false, resolveErrors);
        }

        if (!AgentInstance.TryParse(resolved.Get(ProfileFieldKeys.InstanceName), out AgentInstance instance, out string? instanceError))
        {
            return new InstallationResult(false, new[] { instanceError! });
        }

        if (IsAlreadyInstalled(instance, out string? duplicateError))
        {
            return new InstallationResult(false, new[] { duplicateError! });
        }

        string agentJson;
        try
        {
            agentJson = AgentJsonBuilder.Build(resolved, _protector);
        }
        catch (AgentConfigException ex)
        {
            return new InstallationResult(false, ex.Errors.ToList());
        }

        DeploymentOutcome outcome = _deployer.Install(new InstallationPlan(instance.Name, agentJson));
        return new InstallationResult(outcome.Success, outcome.ReportLines);
    }

    /// <summary>Désinstalle l'instance <paramref name="instanceName"/> (et elle seule), via le déployeur.</summary>
    public DeploymentOutcome Uninstall(string instanceName)
    {
        if (!AgentInstance.TryParse(instanceName, out AgentInstance instance, out string? error))
        {
            return DeploymentOutcome.Failure(error!);
        }

        return _deployer.Uninstall(instance.Name);
    }

    private bool IsAlreadyInstalled(AgentInstance instance, out string? error)
    {
        IReadOnlyList<string> existingInstances;
        try
        {
            existingInstances = _instanceCatalog.ListInstalledInstanceNames();
        }
        catch (InvalidOperationException ex)
        {
            // Énumération impossible (droits insuffisants) : on ne peut pas prouver l'unicité du nom
            // -> bloquer avec le message français déjà porté par le port (CLAUDE.md n°3 & n°12).
            error = ex.Message;
            return true;
        }

        foreach (string existing in existingInstances)
        {
            if (string.Equals(existing, instance.Name, StringComparison.OrdinalIgnoreCase))
            {
                error =
                    $"Une instance « {instance.Name} » est déjà installée sur ce poste " +
                    $"(service « {instance.ServiceName} »). Choisissez un autre nom pour cette installation, " +
                    "ou désinstallez l'instance existante d'abord.";
                return true;
            }
        }

        error = null;
        return false;
    }
}
