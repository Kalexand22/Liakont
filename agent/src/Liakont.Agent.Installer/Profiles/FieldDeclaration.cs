namespace Liakont.Agent.Installer.Profiles;

/// <summary>
/// Déclaration d'un champ dans un profil intégrateur (F13 §5.2) : son <see cref="State"/> et, le cas
/// échéant, la <see cref="DefaultValue"/> par défaut/imposée. Un champ NON déclaré n'a pas
/// d'instance ici — le moteur applique alors le « défaut ouvert » (F13 §5.3).
/// </summary>
internal sealed class FieldDeclaration
{
    public FieldDeclaration(FieldState state, string? defaultValue)
    {
        State = state;
        DefaultValue = defaultValue;
    }

    /// <summary>État d'affichage déclaré (« affiché » / « verrouillé » / « masqué »).</summary>
    public FieldState State { get; }

    /// <summary>
    /// Valeur par défaut/imposée déclarée par le profil, ou <c>null</c> si absente. Pour un champ
    /// « masqué » ou « verrouillé », une valeur est attendue (gardes de <see cref="ProfileValidator"/>).
    /// </summary>
    public string? DefaultValue { get; }

    /// <summary>Vrai si une valeur non vide est déclarée.</summary>
    public bool HasValue => !string.IsNullOrWhiteSpace(DefaultValue);
}
