namespace Liakont.Agent.Installer.Silent;

using System;
using System.IO;
using Liakont.Agent.Installer.Configuration;
using Liakont.Agent.Installer.Profiles;

/// <summary>
/// Installation SILENCIEUSE (sans interface) : pilote le MÊME <see cref="InstallerEngine"/> que le wizard
/// (F13 §3) à partir d'un profil et d'un fichier de réponses, pour les déploiements en masse. Valide
/// d'abord le schéma du profil (un profil embarqué cassé doit échouer bruyamment, jamais masquer un défaut),
/// puis installe. Code de retour : 0 = installé, 1 = problème détecté (à l'image du CLI AGT05).
/// </summary>
internal sealed class SilentInstaller
{
    private const int Ok = 0;
    private const int ProblemDetected = 1;

    private readonly InstallerEngine _engine;

    public SilentInstaller(InstallerEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>
    /// Installe l'instance décrite par <paramref name="profile"/> + <paramref name="input"/>, en écrivant
    /// le déroulé sur <paramref name="output"/>. Rend 0 si l'installation a abouti, 1 sinon.
    /// </summary>
    public int Run(IntegratorProfile profile, InstallationInput input, TextWriter output)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        ProfileValidationResult validation = ProfileValidator.Validate(profile);
        if (!validation.IsValid)
        {
            output.WriteLine($"Profil « {profile.ProfileName} » invalide :");
            foreach (string error in validation.Errors)
            {
                output.WriteLine("  - " + error);
            }

            return ProblemDetected;
        }

        InstallationResult result = _engine.Install(profile, input);
        foreach (string message in result.Messages)
        {
            output.WriteLine(message);
        }

        return result.Success ? Ok : ProblemDetected;
    }
}
