namespace Liakont.Agent.Installer.Silent;

using System;

/// <summary>
/// Levée par <see cref="AnswerFileLoader"/> quand un fichier de réponses du mode silencieux ne peut PAS
/// être interprété : JSON illisible, bloc « valeurs » mal typé, ou clé de champ inconnue. Une clé inconnue
/// est rejetée explicitement (anti-faux-vert, lessons 2026-06-02) : une faute de frappe ne doit jamais
/// retomber silencieusement sur un défaut. Message en français, orienté intégrateur (CLAUDE.md n°12).
/// </summary>
internal sealed class AnswerFileFormatException : Exception
{
    public AnswerFileFormatException(string message)
        : base(message)
    {
    }

    public AnswerFileFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
