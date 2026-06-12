namespace Liakont.Agent.Installer.Configuration;

using System;
using System.Collections.Generic;

/// <summary>
/// Issue d'une installation pilotée par <see cref="InstallerEngine.Install"/> : succès global + messages
/// opérateur français (erreurs bloquantes en cas d'échec ; rapport « check-config » en cas de succès).
/// Partagée par le wizard (affichage à l'écran récapitulatif) et le mode silencieux (sortie + code retour).
/// </summary>
internal sealed class InstallationResult
{
    public InstallationResult(bool success, IReadOnlyList<string> messages)
    {
        Success = success;
        Messages = messages ?? throw new ArgumentNullException(nameof(messages));
    }

    /// <summary>Vrai si l'installation a abouti (config écrite, service installé, check-config vert).</summary>
    public bool Success { get; }

    /// <summary>Messages opérateur français à afficher tels quels.</summary>
    public IReadOnlyList<string> Messages { get; }
}
