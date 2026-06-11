namespace Liakont.Agent.Installer.Profiles;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Résultat de la validation de schéma d'un profil (<see cref="ProfileValidator"/>). Collecte
/// TOUTES les erreurs (à la façon de <c>AgentConfigLoader</c>) pour que l'intégrateur les voie d'un
/// coup au packaging (OPS08c), plutôt qu'une à la fois.
/// </summary>
internal sealed class ProfileValidationResult
{
    private ProfileValidationResult(IReadOnlyList<string> errors)
    {
        Errors = errors;
    }

    /// <summary>Erreurs de schéma, en français (vide si le profil est valide).</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Vrai si aucune erreur n'a été relevée.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>Construit un résultat à partir des erreurs collectées.</summary>
    public static ProfileValidationResult FromErrors(IEnumerable<string> errors)
    {
        return new ProfileValidationResult(errors.ToList());
    }
}
