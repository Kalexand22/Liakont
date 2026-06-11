namespace Liakont.Agent.Installer.Profiles;

using System;

/// <summary>
/// Levée par <see cref="IntegratorProfileLoader"/> quand un profil ne peut PAS être interprété :
/// JSON malformé, bloc « champs » mal typé, ou état (« etat ») inconnu. Distincte des erreurs de
/// SCHÉMA (<see cref="ProfileValidator"/>) qui, elles, supposent un profil bien formé. Message en
/// français, orienté intégrateur (CLAUDE.md n°12).
/// </summary>
internal sealed class ProfileFormatException : Exception
{
    public ProfileFormatException(string message)
        : base(message)
    {
    }

    public ProfileFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
