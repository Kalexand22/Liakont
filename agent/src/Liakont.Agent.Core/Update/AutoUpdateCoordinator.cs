namespace Liakont.Agent.Core.Update;

using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Logging;
using Liakont.Agent.Core.Time;

/// <summary>
/// Orchestre l'auto-update de l'agent (AGT04, F12 §2.5, ADR-0013) : télécharge le manifeste, VÉRIFIE sa
/// signature (clé de release provisionnée — fail-closed) puis l'empreinte SHA-256 du paquet, garde
/// l'anti-downgrade, DIFFÈRE tant qu'un run tourne, puis lance l'updater DÉTACHÉ. Aucune logique
/// métier (CLAUDE.md n°6) : il applique des règles mécaniques sur un paquet, ne lève jamais
/// (toute défaillance est une <see cref="AutoUpdateOutcome"/>), et signale chaque issue à l'opérateur
/// (log français + fichier statut relu par le heartbeat).
/// </summary>
public sealed class AutoUpdateCoordinator : IAutoUpdateService
{
    private readonly IUpdatePackageSource _packageSource;
    private readonly IManifestSignatureVerifier _signatureVerifier;
    private readonly IRunActivityProbe _runProbe;
    private readonly IUpdaterLauncher _launcher;
    private readonly AutoUpdateStateStore _statusStore;
    private readonly AutoUpdateEnvironment _environment;
    private readonly IClock _clock;
    private readonly IAgentLog _log;

    private int _inProgress;
    private int _pushUpgradeRequired;

    /// <summary>Crée un coordinateur d'auto-update.</summary>
    /// <param name="packageSource">Source de téléchargement (manifeste + paquet).</param>
    /// <param name="signatureVerifier">Vérificateur de signature du manifeste.</param>
    /// <param name="runProbe">Sonde de run en cours (garde « jamais d'update pendant un run »).</param>
    /// <param name="launcher">Lanceur de l'updater détaché.</param>
    /// <param name="statusStore">Store du statut (signalement heartbeat).</param>
    /// <param name="environment">Paramètres d'environnement (chemins, version, service).</param>
    /// <param name="clock">Horloge.</param>
    /// <param name="log">Journal de l'agent (messages opérateur français).</param>
    public AutoUpdateCoordinator(
        IUpdatePackageSource packageSource,
        IManifestSignatureVerifier signatureVerifier,
        IRunActivityProbe runProbe,
        IUpdaterLauncher launcher,
        AutoUpdateStateStore statusStore,
        AutoUpdateEnvironment environment,
        IClock clock,
        IAgentLog log)
    {
        _packageSource = packageSource ?? throw new ArgumentNullException(nameof(packageSource));
        _signatureVerifier = signatureVerifier ?? throw new ArgumentNullException(nameof(signatureVerifier));
        _runProbe = runProbe ?? throw new ArgumentNullException(nameof(runProbe));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _statusStore = statusStore ?? throw new ArgumentNullException(nameof(statusStore));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc/>
    public AutoUpdateResult ConsiderHeartbeatConfiguration(AgentConfigurationDto configuration)
    {
        if (configuration == null)
        {
            return new AutoUpdateResult(AutoUpdateOutcome.NotRequested, "Aucune configuration plateforme.");
        }

        bool requested = configuration.UpdateRequired || Volatile.Read(ref _pushUpgradeRequired) == 1;
        if (!requested)
        {
            return new AutoUpdateResult(AutoUpdateOutcome.NotRequested, "Aucune mise à jour requise.");
        }

        return Run(configuration);
    }

    /// <inheritdoc/>
    public void RecordPushUpgradeRequired()
    {
        if (Interlocked.Exchange(ref _pushUpgradeRequired, 1) == 0)
        {
            _log.Warn("Mise à jour requise signalée par la plateforme (426) — elle partira au prochain cycle de heartbeat, hors run d'extraction.");
        }
    }

    /// <inheritdoc/>
    public AutoUpdateStatus? GetLatestStatus() => _statusStore.TryGetLatest();

    private static bool TryCompareVersions(string candidate, string current, out bool isNewer)
    {
        isNewer = false;
        if (string.IsNullOrWhiteSpace(candidate) || !Version.TryParse(candidate.Trim(), out Version candidateVersion))
        {
            return false;
        }

        if (!Version.TryParse((current ?? string.Empty).Trim(), out Version currentVersion))
        {
            currentVersion = new Version(0, 0, 0, 0);
        }

        isNewer = candidateVersion > currentVersion;
        return true;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private AutoUpdateResult Run(AgentConfigurationDto configuration)
    {
        if (Interlocked.CompareExchange(ref _inProgress, 1, 0) != 0)
        {
            return new AutoUpdateResult(AutoUpdateOutcome.AlreadyInProgress, "Une mise à jour est déjà en cours dans ce processus.");
        }

        TryPurgeStaleWorkDirectories();

        bool launched = false;
        string? attemptDir = null;
        try
        {
            if (string.IsNullOrWhiteSpace(configuration.UpdateUrl))
            {
                return Signal(AutoUpdateOutcome.NoManifestUrl, null, false, "Mise à jour requise mais aucune URL de manifeste fournie par la plateforme.");
            }

            if (_runProbe.IsRunInProgress())
            {
                // Différé : on NE consomme PAS le fanion 426 — la mise à jour repartira au cycle suivant.
                return Signal(AutoUpdateOutcome.DeferredRunInProgress, null, true, "Mise à jour différée : une extraction est en cours (jamais d'update pendant un run).");
            }

            // On s'engage à traiter la demande : le fanion 426 est consommé (la config pilotera les retours suivants).
            Interlocked.Exchange(ref _pushUpgradeRequired, 0);

            if (!_signatureVerifier.HasKey)
            {
                return Signal(AutoUpdateOutcome.MissingSigningKey, null, false, "Mise à jour refusée : aucune clé de signature provisionnée (sécurité — fail-closed).");
            }

            byte[]? manifestBytes = _packageSource.TryDownloadManifest(configuration.UpdateUrl!);
            if (manifestBytes == null)
            {
                return Signal(AutoUpdateOutcome.DownloadFailed, null, false, "Échec du téléchargement du manifeste de mise à jour.");
            }

            if (!_signatureVerifier.Verify(manifestBytes, configuration.VersionManifestSignature))
            {
                return Signal(AutoUpdateOutcome.RejectedSignature, null, false, "Mise à jour refusée : signature du manifeste invalide (provenance non prouvée).");
            }

            if (!VersionManifest.TryParse(manifestBytes, out VersionManifest? manifest))
            {
                return Signal(AutoUpdateOutcome.InvalidManifest, null, false, "Mise à jour refusée : manifeste illisible ou incomplet.");
            }

            if (!TryCompareVersions(manifest!.Version, _environment.CurrentVersion, out bool isNewer))
            {
                return Signal(AutoUpdateOutcome.InvalidManifest, manifest.Version, false, $"Mise à jour refusée : version de manifeste illisible ({manifest.Version}).");
            }

            if (!isNewer)
            {
                return Signal(AutoUpdateOutcome.AlreadyCurrent, manifest.Version, true, $"Aucune mise à jour : la version proposée ({manifest.Version}) n'est pas plus récente que l'actuelle ({_environment.CurrentVersion}).");
            }

            attemptDir = Path.Combine(_environment.WorkRootDirectory, "attempt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(attemptDir);
            string packagePath = Path.Combine(attemptDir, "package.zip");

            if (!_packageSource.TryDownloadPackage(manifest.PackageUrl, packagePath))
            {
                return Signal(AutoUpdateOutcome.DownloadFailed, manifest.Version, false, "Échec du téléchargement du paquet de mise à jour.");
            }

            if (!PackageHashVerifier.Matches(packagePath, manifest.PackageSha256))
            {
                return Signal(AutoUpdateOutcome.RejectedHash, manifest.Version, false, "Mise à jour refusée : empreinte SHA-256 du paquet non concordante avec le manifeste signé.");
            }

            string stagingDir = Path.Combine(attemptDir, "staging");
            ZipFile.ExtractToDirectory(packagePath, stagingDir);

            string backupDir = Path.Combine(attemptDir, "backup");
            var request = new UpdaterLaunchRequest(
                manifest.Version,
                stagingDir,
                _environment.InstallDirectory,
                backupDir,
                _environment.ServiceName,
                _environment.HealthTimeout,
                _environment.UpdaterLogPath,
                _environment.StatusPath,
                _environment.HeartbeatMarkerPath);

            if (!_launcher.Launch(request))
            {
                return Signal(AutoUpdateOutcome.Failed, manifest.Version, false, "Échec du lancement de l'updater détaché.");
            }

            launched = true;

            // Statut « lancé » NON succès : seul l'updater confirmera (Applied) ou infirmera (RolledBack)
            // en réécrivant ce fichier. Si l'updater meurt avant, ce statut « en attente » remonte au
            // heartbeat plutôt qu'un faux succès. Le verrou _inProgress reste tenu (le process va être
            // remplacé) ; un updater qui meurt instantanément laisse l'agent dans cet état jusqu'au
            // prochain (re)démarrage du service — surfacé, donc diagnosticable.
            string launchMessage = $"Mise à jour vers {manifest.Version} lancée (updater détaché) — en attente de confirmation du redémarrage.";
            _log.Info(launchMessage);
            _statusStore.Record(new AutoUpdateStatus(manifest.Version, AutoUpdateOutcome.Launched.ToString(), succeeded: false, launchMessage, _clock.UtcNow));
            return new AutoUpdateResult(AutoUpdateOutcome.Launched, launchMessage, manifest.Version);
        }
        catch (Exception ex)
        {
            return Signal(AutoUpdateOutcome.Failed, null, false, "Échec inattendu de la mise à jour : " + ex.Message);
        }
        finally
        {
            // Après un lancement réussi, on garde le verrou (le processus va être remplacé) : aucun
            // second updater ne doit partir. Pour toute autre issue, on libère pour réessayer plus tard.
            if (!launched)
            {
                Interlocked.Exchange(ref _inProgress, 0);
                if (attemptDir != null)
                {
                    TryDeleteDirectory(attemptDir);
                }
            }
        }
    }

    private void TryPurgeStaleWorkDirectories()
    {
        try
        {
            if (!Directory.Exists(_environment.WorkRootDirectory))
            {
                return;
            }

            foreach (string directory in Directory.GetDirectories(_environment.WorkRootDirectory))
            {
                string name = Path.GetFileName(directory);
                if (name.StartsWith("attempt-", StringComparison.Ordinal) || name.StartsWith("updater-", StringComparison.Ordinal))
                {
                    TryDeleteDirectory(directory);
                }
            }
        }
        catch (IOException)
        {
            // Purge best-effort : un dossier encore verrouillé sera repris au prochain passage.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private AutoUpdateResult Signal(AutoUpdateOutcome outcome, string? version, bool succeeded, string message)
    {
        if (succeeded)
        {
            _log.Info(message);
        }
        else
        {
            _log.Warn(message);
        }

        _statusStore.Record(new AutoUpdateStatus(version, outcome.ToString(), succeeded, message, _clock.UtcNow));
        return new AutoUpdateResult(outcome, message, version);
    }
}
