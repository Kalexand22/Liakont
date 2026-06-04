namespace Liakont.Modules.Archive.Domain;

/// <summary>
/// Méthode d'ancrage temporel de la tête de chaîne du coffre (TRK06). L'ancrage est un AXE ENFICHABLE à
/// capacités, choisi par l'éditeur au niveau INSTANCE (même principe que les plug-ins PA et que
/// <see cref="IArchiveStore"/>) : le module pilote son comportement par les
/// <see cref="TimestampAnchorCapabilities"/> déclarées, JAMAIS par un test de type concret.
/// </summary>
public enum TimestampAnchorMethod
{
    /// <summary>Aucun ancrage : l'intégrité repose entièrement sur la chaîne de hashes (instance sans accès internet sortant).</summary>
    None = 0,

    /// <summary>Horodatage qualifié RFC 3161 via une TSA configurable (eIDAS), API natives .NET, sans dépendance.</summary>
    Rfc3161 = 1,

    /// <summary>Ancrage blockchain OpenTimestamps — reporté en V1.1 (ADR-0010), non opérationnel en V1.</summary>
    OpenTimestamps = 2,
}
