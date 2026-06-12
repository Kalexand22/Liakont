namespace Liakont.Agent.Installer.Configuration;

using System;
using System.Collections.Generic;
using Liakont.Agent.Installer.Profiles;

/// <summary>
/// Fusionne un profil intégrateur (visibilité par champ) et les valeurs saisies en une
/// <see cref="ResolvedConfiguration"/>, par ITÉRATION sur les champs résolus du moteur de profil
/// (data-driven, jamais d'<c>if(integrateur)</c>) :
/// <list type="bullet">
///   <item>champ « affiché » → valeur saisie, à défaut la valeur par défaut du profil ;</item>
///   <item>champ « verrouillé »/« masqué » → valeur imposée par le profil (la saisie est ignorée).</item>
/// </list>
/// Collecte aussi les erreurs bloquantes : un champ requis (platformUrl, apiKey) non résolu rend
/// l'agent non fonctionnel (F13 §5.3) — on bloque plutôt que d'installer une configuration muette.
/// </summary>
internal static class ConfigurationResolver
{
    /// <summary>Résout la configuration effective et collecte les erreurs bloquantes (liste vide = valide).</summary>
    public static ResolvedConfiguration Resolve(
        IntegratorProfile profile,
        InstallationInput input,
        out IReadOnlyList<string> errors)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var engine = new IntegratorProfileEngine(profile);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (ResolvedField field in engine.ResolveAll())
        {
            // « affiché » : la saisie prime, avec repli sur un éventuel pré-remplissage du profil.
            // « verrouillé »/« masqué » : valeur imposée par le profil — la saisie est ignorée (F13 §5.2).
            string? effective = field.IsEditable
                ? input.Get(field.Key) ?? field.DefaultValue
                : field.DefaultValue;
            values[field.Key] = effective;
        }

        var errorList = new List<string>();
        foreach (string required in ProfileFieldKeys.Required)
        {
            values.TryGetValue(required, out string? value);
            if (string.IsNullOrWhiteSpace(value))
            {
                errorList.Add(
                    $"Le champ requis « {required} » n'est pas renseigné. " +
                    "L'agent serait non fonctionnel — complétez-le avant d'installer.");
            }
        }

        errors = errorList;
        return new ResolvedConfiguration(values);
    }
}
