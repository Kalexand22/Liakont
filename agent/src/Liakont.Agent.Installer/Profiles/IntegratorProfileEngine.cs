namespace Liakont.Agent.Installer.Profiles;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Moteur de profil intégrateur DÉCLARATIF (F13 §5.1) : étant donné un <see cref="IntegratorProfile"/>,
/// il résout, par champ, l'état effectif et la valeur à présenter — par ITÉRATION sur les champs
/// déclarés, sans aucune branche conditionnelle codée en dur sur l'identité de l'intégrateur (pas de
/// <c>if(integrateur)</c>). Le comportement de l'installeur est ainsi piloté par les DONNÉES du
/// profil, exactement comme les capacités PA pilotent le comportement produit (blueprint.md §2).
/// <para>
/// Règle du DÉFAUT OUVERT (F13 §5.3) : un champ non déclaré est « affiché » et éditable — l'intégrateur
/// voit tout, sauf ce qu'on masque/verrouille explicitement.
/// </para>
/// </summary>
internal sealed class IntegratorProfileEngine
{
    private readonly IntegratorProfile _profile;

    public IntegratorProfileEngine(IntegratorProfile profile)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    /// <summary>
    /// Résout un champ par sa clé. Champ déclaré → état + valeur déclarés ; champ non déclaré →
    /// « affiché », éditable, sans valeur (défaut ouvert).
    /// </summary>
    public ResolvedField Resolve(string key)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (_profile.Fields.TryGetValue(key, out FieldDeclaration? declaration))
        {
            return new ResolvedField(key, declaration.State, declaration.DefaultValue);
        }

        return new ResolvedField(key, FieldState.Shown, defaultValue: null);
    }

    /// <summary>
    /// Résout l'ensemble des champs : l'union des clés CONNUES (<see cref="ProfileFieldKeys.All"/>) et
    /// des clés déclarées par le profil, triée pour un rendu stable. Le moteur n'énumère pas les
    /// champs en dur — il itère sur cette union, d'où l'extensibilité « ajouter une clé sans toucher
    /// au moteur » (F13 §5.4).
    /// </summary>
    public IReadOnlyList<ResolvedField> ResolveAll()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string key in ProfileFieldKeys.All)
        {
            keys.Add(key);
        }

        foreach (string key in _profile.Fields.Keys)
        {
            keys.Add(key);
        }

        return keys.Select(Resolve).ToList();
    }
}
