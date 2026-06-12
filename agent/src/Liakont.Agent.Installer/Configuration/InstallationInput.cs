namespace Liakont.Agent.Installer.Configuration;

using System;
using System.Collections.Generic;

/// <summary>
/// Valeurs saisies pour une installation, indexées par clé de champ (<see cref="Profiles.ProfileFieldKeys"/>).
/// Source UNIQUE alimentant le moteur (<see cref="InstallerEngine"/>), que les valeurs viennent du wizard
/// ou du fichier de réponses (mode silencieux) — d'où « le wizard et le mode silencieux partagent le même
/// moteur de configuration » (F13 §3). Les secrets (clé API, ODBC) y transitent en clair en mémoire, puis
/// sont chiffrés DPAPI à la construction de <c>agent.json</c> — jamais écrits ni journalisés en clair.
/// </summary>
internal sealed class InstallationInput
{
    private readonly IReadOnlyDictionary<string, string?> _values;

    public InstallationInput(IReadOnlyDictionary<string, string?> values)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    /// <summary>Entrée vide (aucun champ saisi) — utile en test et pour un wizard parti d'un profil tout-affiché.</summary>
    public static InstallationInput Empty { get; } =
        new InstallationInput(new Dictionary<string, string?>(StringComparer.Ordinal));

    /// <summary>Valeur saisie pour <paramref name="key"/>, ou <c>null</c> si non renseignée.</summary>
    public string? Get(string key)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return _values.TryGetValue(key, out string? value) ? value : null;
    }
}
