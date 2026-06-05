namespace Liakont.Agent.Core.Extraction;

using System;

/// <summary>
/// Identité d'un extracteur source (F01-F02 §4.1) — affichée en console (CLI) et journalisée. Données
/// purement descriptives, aucune logique.
/// </summary>
public sealed class ExtractorInfo
{
    /// <summary>Crée une identité d'extracteur.</summary>
    /// <param name="name">Nom de l'adaptateur (ex. « EncheresV6 », « Fixture »).</param>
    /// <param name="version">Version de l'adaptateur.</param>
    /// <param name="targetSystem">Système cible décrit (ex. « Magic XPA / Pervasive »).</param>
    public ExtractorInfo(string name, string version, string targetSystem)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Version = version ?? throw new ArgumentNullException(nameof(version));
        TargetSystem = targetSystem ?? throw new ArgumentNullException(nameof(targetSystem));
    }

    /// <summary>Nom de l'adaptateur.</summary>
    public string Name { get; }

    /// <summary>Version de l'adaptateur.</summary>
    public string Version { get; }

    /// <summary>Système cible décrit.</summary>
    public string TargetSystem { get; }
}
