namespace Liakont.Agent.Installer.Profiles;

/// <summary>
/// Résultat de la résolution d'un champ par <see cref="IntegratorProfileEngine"/> : l'état effectif
/// du champ et sa valeur par défaut, tels que le wizard (OPS08b) doit les présenter. C'est la sortie
/// que consomment les écrans — ils n'interprètent jamais le profil eux-mêmes.
/// </summary>
internal sealed class ResolvedField
{
    public ResolvedField(string key, FieldState state, string? defaultValue)
    {
        Key = key;
        State = state;
        DefaultValue = defaultValue;
    }

    /// <summary>Clé du champ (« adapter », « platformUrl », …).</summary>
    public string Key { get; }

    /// <summary>État effectif après application du profil (défaut ouvert si non déclaré).</summary>
    public FieldState State { get; }

    /// <summary>Valeur par défaut/imposée, ou <c>null</c>.</summary>
    public string? DefaultValue { get; }

    /// <summary>Le champ est visible dans le wizard (tout état sauf « masqué »).</summary>
    public bool IsVisible => State != FieldState.Hidden;

    /// <summary>Le champ est éditable par l'intégrateur (« affiché » uniquement).</summary>
    public bool IsEditable => State == FieldState.Shown;
}
