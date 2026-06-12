namespace Liakont.Agent.Installer.Profiles;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Résout le profil intégrateur à appliquer AU DÉMARRAGE de l'installeur, à partir du manifeste JSON
/// éventuellement embarqué (F13 §7, OPS08c). Logique PURE et testable : la lecture native de la ressource
/// est faite par <see cref="EmbeddedProfile"/> et passée ici en argument (<paramref name="embeddedJson"/>).
/// <list type="bullet">
///   <item>aucun profil embarqué (<c>null</c>/vide) → profil par DÉFAUT OUVERT (tous champs affichés et
///   éditables, F13 §5.3) — l'installeur de développement et le paquet sans profil se comportent comme
///   avant OPS08c ;</item>
///   <item>profil embarqué bien formé ET valide → ce profil ;</item>
///   <item>profil embarqué malformé ou invalide → ÉCHEC (<c>error</c> renseigné) : on ne retombe JAMAIS
///   silencieusement sur le profil ouvert (un profil corrompu rouvrirait des écrans que l'intégrateur a
///   verrouillés — faux vert, F13 §5.3 ; lessons 2026-06-02). Le packaging valide déjà chaque profil
///   AVANT injection, donc ce cas ne survient qu'en cas d'altération.</item>
/// </list>
/// </summary>
internal static class StartupProfileResolver
{
    private const string EmbeddedSourceName = "(profil intégrateur embarqué)";

    /// <summary>Profil par défaut ouvert : aucun champ déclaré → tout est affiché et éditable (F13 §5.3).</summary>
    public static IntegratorProfile DefaultOpenProfile() =>
        new IntegratorProfile(
            "(profil par défaut)",
            IntegratorBranding.Empty,
            new Dictionary<string, FieldDeclaration>(StringComparer.Ordinal));

    /// <summary>
    /// Résout le profil de démarrage. Rend <c>true</c> et un <paramref name="profile"/> exploitable quand
    /// il n'y a pas de profil embarqué (défaut ouvert) ou quand le profil embarqué est valide. Rend
    /// <c>false</c> et <paramref name="error"/> (message français) si le profil embarqué est malformé ou
    /// invalide — l'appelant doit alors interrompre, jamais utiliser le profil.
    /// </summary>
    public static bool TryResolve(string? embeddedJson, out IntegratorProfile profile, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(embeddedJson))
        {
            profile = DefaultOpenProfile();
            return true;
        }

        try
        {
            profile = IntegratorProfileLoader.Parse(embeddedJson!, EmbeddedSourceName);
        }
        catch (ProfileFormatException ex)
        {
            profile = DefaultOpenProfile();
            error = ex.Message;
            return false;
        }

        ProfileValidationResult validation = ProfileValidator.Validate(profile);
        if (!validation.IsValid)
        {
            error = $"Profil intégrateur embarqué « {profile.ProfileName} » invalide :" + Environment.NewLine +
                string.Join(Environment.NewLine, validation.Errors.Select(e => "  - " + e));
            return false;
        }

        return true;
    }
}
