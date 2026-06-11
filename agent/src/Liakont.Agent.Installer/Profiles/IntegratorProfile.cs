namespace Liakont.Agent.Installer.Profiles;

using System.Collections.Generic;

/// <summary>
/// Profil intégrateur déclaratif (F13 §5) : branding + visibilité par champ. Le profil ne porte QUE
/// de la présentation et de la visibilité — aucun secret, aucune donnée client (F13 §5.5). Il est
/// produit par <see cref="IntegratorProfileLoader"/>, validé par <see cref="ProfileValidator"/>, et
/// interprété par <see cref="IntegratorProfileEngine"/>.
/// </summary>
internal sealed class IntegratorProfile
{
    public IntegratorProfile(
        string profileName,
        IntegratorBranding branding,
        IReadOnlyDictionary<string, FieldDeclaration> fields)
    {
        ProfileName = profileName;
        Branding = branding;
        Fields = fields;
    }

    /// <summary>Nom du profil (« profil » du manifeste), pour les messages opérateur.</summary>
    public string ProfileName { get; }

    /// <summary>Branding (présentation) déclaré par le profil.</summary>
    public IntegratorBranding Branding { get; }

    /// <summary>
    /// Champs déclarés, indexés par clé (« adapter », « platformUrl », …). Un champ ABSENT de ce
    /// dictionnaire suit le « défaut ouvert » du moteur (F13 §5.3) ; le moteur itère sur les clés —
    /// il ne les énumère pas en dur, d'où l'extensibilité « ajouter une clé sans toucher au moteur »
    /// (F13 §5.4).
    /// </summary>
    public IReadOnlyDictionary<string, FieldDeclaration> Fields { get; }
}
