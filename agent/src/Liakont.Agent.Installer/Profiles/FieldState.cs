namespace Liakont.Agent.Installer.Profiles;

/// <summary>
/// État d'affichage d'un champ de l'installeur, gouverné par le profil intégrateur (F13 §5.2).
/// Le moteur de profil (<see cref="IntegratorProfileEngine"/>) traduit cet état en visibilité et
/// éditabilité du champ dans le wizard (OPS08b), sans aucune branche conditionnelle codée en dur
/// sur l'identité de l'intégrateur (F13 §5.1).
/// </summary>
internal enum FieldState
{
    /// <summary>« affiché » : le champ est visible et éditable par l'intégrateur (défaut ouvert).</summary>
    Shown,

    /// <summary>« verrouillé » : le champ est visible mais non éditable (valeur imposée par le profil).</summary>
    Locked,

    /// <summary>« masqué » : le champ n'est pas affiché ; sa valeur est imposée par le profil.</summary>
    Hidden,
}
