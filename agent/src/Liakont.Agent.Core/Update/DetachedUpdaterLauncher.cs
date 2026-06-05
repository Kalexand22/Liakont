namespace Liakont.Agent.Core.Update;

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// Lance <c>Liakont.Agent.Updater.exe</c> en processus détaché (ADR-0013). Avant de démarrer, il COPIE
/// le dossier de l'updater dans un dossier de travail séparé puis l'exécute DE LÀ : un binaire chargé
/// ne peut pas se remplacer lui-même, et l'updater doit survivre à l'arrêt du service qu'il pilote.
/// </summary>
public sealed class DetachedUpdaterLauncher : IUpdaterLauncher
{
    private readonly string _installedUpdaterExePath;
    private readonly string _workingRootDirectory;

    /// <summary>Crée un lanceur d'updater détaché.</summary>
    /// <param name="installedUpdaterExePath">Chemin de l'updater installé (copié avant exécution).</param>
    /// <param name="workingRootDirectory">Racine des dossiers de travail de l'updater (hors dossier d'installation).</param>
    public DetachedUpdaterLauncher(string installedUpdaterExePath, string workingRootDirectory)
    {
        _installedUpdaterExePath = installedUpdaterExePath ?? throw new ArgumentNullException(nameof(installedUpdaterExePath));
        _workingRootDirectory = workingRootDirectory ?? throw new ArgumentNullException(nameof(workingRootDirectory));
    }

    /// <inheritdoc/>
    public bool Launch(UpdaterLaunchRequest request)
    {
        if (request == null)
        {
            return false;
        }

        try
        {
            string sourceDir = Path.GetDirectoryName(_installedUpdaterExePath)!;
            string exeName = Path.GetFileName(_installedUpdaterExePath);
            string workingDir = Path.Combine(_workingRootDirectory, "updater-" + Guid.NewGuid().ToString("N"));
            CopyDirectory(sourceDir, workingDir);

            string runExe = Path.Combine(workingDir, exeName);
            var startInfo = new ProcessStartInfo
            {
                FileName = runExe,
                Arguments = BuildArguments(request),
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDir,
            };

            Process? process = Process.Start(startInfo);

            // On ne conserve pas le handle : l'updater est DÉTACHÉ (il vit après l'arrêt du service/agent).
            process?.Dispose();
            return process != null;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string BuildArguments(UpdaterLaunchRequest request)
    {
        var builder = new StringBuilder();
        AppendOption(builder, "--target-version", request.TargetVersion);
        AppendOption(builder, "--staging", request.StagingDirectory);
        AppendOption(builder, "--install", request.InstallDirectory);
        AppendOption(builder, "--backup", request.BackupDirectory);
        AppendOption(builder, "--service", request.ServiceName);
        AppendOption(builder, "--health-timeout-seconds", ((int)request.HealthTimeout.TotalSeconds).ToString(CultureInfo.InvariantCulture));
        AppendOption(builder, "--log", request.LogPath);
        AppendOption(builder, "--status", request.StatusPath);
        AppendOption(builder, "--heartbeat-marker", request.HeartbeatMarkerPath);
        return builder.ToString().Trim();
    }

    private static void AppendOption(StringBuilder builder, string name, string value)
    {
        builder.Append(name).Append(" \"").Append(value).Append("\" ");
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(subDir, Path.Combine(destinationDir, Path.GetFileName(subDir)));
        }
    }
}
