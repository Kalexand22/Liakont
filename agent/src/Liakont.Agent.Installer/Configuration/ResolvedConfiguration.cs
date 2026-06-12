namespace Liakont.Agent.Installer.Configuration;

using System;
using System.Collections.Generic;

/// <summary>
/// Configuration EFFECTIVE d'une installation, après fusion du profil intégrateur (visibilité par
/// <see cref="Profiles.IntegratorProfileEngine"/>) et des valeurs saisies (<see cref="InstallationInput"/>) :
/// par champ, la valeur réellement retenue. Un champ « verrouillé »/« masqué » porte la valeur imposée
/// par le profil ; un champ « affiché » porte la saisie (à défaut, le défaut du profil). C'est l'entrée
/// que consomme <see cref="AgentJsonBuilder"/>. Production par <see cref="ConfigurationResolver"/>.
/// </summary>
internal sealed class ResolvedConfiguration
{
    private readonly IReadOnlyDictionary<string, string?> _values;

    public ResolvedConfiguration(IReadOnlyDictionary<string, string?> values)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    /// <summary>Valeur effective du champ <paramref name="key"/>, ou <c>null</c> si non résolue.</summary>
    public string? Get(string key)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return _values.TryGetValue(key, out string? value) ? value : null;
    }
}
