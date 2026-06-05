namespace Liakont.Agent.Core.Configuration;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

/// <summary>
/// Erreur de configuration de l'agent : fichier introuvable, JSON illisible, ou champs invalides.
/// Porte la liste des messages opérateur EN FRANÇAIS (CLAUDE.md n°12), chacun nommant le champ
/// fautif et l'action corrective. Bloquer plutôt que démarrer avec une config fausse (CLAUDE.md n°3).
/// </summary>
public sealed class AgentConfigException : Exception
{
    public AgentConfigException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = new ReadOnlyCollection<string>(errors.ToList());
    }

    public AgentConfigException(string error)
        : this(new[] { error })
    {
    }

    /// <summary>Les messages opérateur français, un par problème détecté.</summary>
    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IReadOnlyList<string> errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return "Configuration de l'agent invalide.";
        }

        return "Configuration de l'agent invalide :" + Environment.NewLine
            + string.Join(Environment.NewLine, errors.Select(e => "  - " + e));
    }
}
