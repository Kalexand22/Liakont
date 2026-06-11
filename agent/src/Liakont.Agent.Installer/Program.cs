namespace Liakont.Agent.Installer;

using System;
using System.IO;
using System.Runtime.InteropServices;
using Liakont.Agent.Installer.Profiles;

/// <summary>
/// Point d'entrée du bootstrapper d'installation de l'agent (F13). OPS08a livre le projet, le moteur
/// de profil intégrateur et la validation de schéma ; les écrans guidés (wizard WinForms) sont livrés
/// par OPS08b, le packaging multi-profils par OPS08c. En attendant le wizard, le point d'entrée
/// n'expose qu'un mode sans interface « --validate &lt;profil&gt; » — réutilisé par le packaging
/// (OPS08c) et par le smoke-test de l'item — qui valide un profil et affiche son plan de champs.
/// </summary>
internal static class Program
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;

    [STAThread]
    internal static int Main(string[] args)
    {
        // N'attache la console du parent que si stdout n'est PAS déjà redirigé : préserve une
        // redirection explicite du parent (contrat d'automatisation OPS08c « capturer stdout/stderr »),
        // tout en rendant les messages français visibles quand « --validate » est lancé nu depuis un
        // terminal (un exe GUI n'a alors aucun handle stdout). Sans console parente (futur wizard lancé
        // depuis l'explorateur), AttachConsole échoue sans effet.
        IntPtr standardOutput = GetStdHandle(StdOutputHandle);
        if (standardOutput == IntPtr.Zero || standardOutput == new IntPtr(-1))
        {
            AttachConsole(AttachParentProcess);
        }

        if (args.Length >= 1 && string.Equals(args[0], "--validate", StringComparison.OrdinalIgnoreCase))
        {
            return RunValidate(args);
        }

        Console.Error.WriteLine(
            "Liakont.Agent.Installer : le wizard d'installation guidé sera disponible avec OPS08b. " +
            "Usage actuel : Liakont.Agent.Installer.exe --validate <chemin-du-profil.json>");
        return 2;
    }

    private static int RunValidate(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage : Liakont.Agent.Installer.exe --validate <chemin-du-profil.json>");
            return 2;
        }

        string path = args[1];
        IntegratorProfile profile;
        try
        {
            profile = IntegratorProfileLoader.Load(path);
        }
        catch (ProfileFormatException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Lecture du profil « {path} » impossible : {ex.Message}");
            return 2;
        }

        ProfileValidationResult result = ProfileValidator.Validate(profile);
        if (!result.IsValid)
        {
            Console.Error.WriteLine($"Profil « {profile.ProfileName} » invalide :");
            foreach (string error in result.Errors)
            {
                Console.Error.WriteLine($"  - {error}");
            }

            return 1;
        }

        Console.Out.WriteLine($"Profil « {profile.ProfileName} » valide. Plan des champs :");
        var engine = new IntegratorProfileEngine(profile);
        foreach (ResolvedField field in engine.ResolveAll())
        {
            string state = DescribeState(field.State);
            string editable = field.IsEditable ? "éditable" : "non éditable";
            string value = field.DefaultValue == null ? "(saisie au wizard)" : $"« {field.DefaultValue} »";
            Console.Out.WriteLine($"  - {field.Key} : {state}, {editable}, défaut {value}");
        }

        return 0;
    }

    private static string DescribeState(FieldState state)
    {
        switch (state)
        {
            case FieldState.Shown:
                return "affiché";
            case FieldState.Locked:
                return "verrouillé";
            case FieldState.Hidden:
                return "masqué";
            default:
                return state.ToString();
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetStdHandle(int nStdHandle);
}
