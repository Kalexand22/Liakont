namespace Liakont.Agent.Installer;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Liakont.Agent.Cli;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Security;
using Liakont.Agent.Installer.Configuration;
using Liakont.Agent.Installer.Deployment;
using Liakont.Agent.Installer.Profiles;
using Liakont.Agent.Installer.Silent;
using Liakont.Agent.Installer.Wizard;

/// <summary>
/// Point d'entrée du bootstrapper d'installation de l'agent (F13). Sans argument : ouvre le WIZARD GUI
/// (OPS08b §4). Modes sans interface (composition root des ports de production) :
/// <list type="bullet">
///   <item><c>--silent &lt;réponses.json&gt; [--profile &lt;p&gt;]</c> : installation silencieuse (mode partagé) ;</item>
///   <item><c>--uninstall &lt;instance&gt;</c> : désinstallation d'UNE instance (multi-instances) ;</item>
///   <item><c>--list-instances</c> : liste les instances installées sur ce poste ;</item>
///   <item><c>--validate &lt;profil.json&gt;</c> : valide un profil et affiche son plan de champs (OPS08a, réutilisé par OPS08c) ;</item>
///   <item><c>--show-profile</c> : affiche le profil intégrateur EMBARQUÉ dans cet exécutable (OPS08c) — diagnostic de packaging.</item>
/// </list>
/// Le wizard et le mode silencieux partagent le MÊME <see cref="InstallerEngine"/> (F13 §3).
/// </summary>
internal static class Program
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;

    [STAThread]
    internal static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return RunWizard();
        }

        EnsureConsoleForCli();

        switch (args[0].ToLowerInvariant())
        {
            case "--validate":
                return RunValidate(args);
            case "--silent":
                return RunSilent(args);
            case "--uninstall":
                return RunUninstall(args);
            case "--list-instances":
                return RunListInstances();
            case "--show-profile":
                return RunShowProfile();
            default:
                Console.Error.WriteLine(
                    "Usage : Liakont.Agent.Installer.exe [sans argument = wizard] | " +
                    "--silent <réponses.json> [--profile <profil.json>] | --uninstall <instance> | " +
                    "--list-instances | --validate <profil.json> | --show-profile");
                return 2;
        }
    }

    private static int RunWizard()
    {
        InstallerEngine engine = BuildEngine();

        // Profil intégrateur EMBARQUÉ (OPS08c) s'il y en a un, sinon profil par défaut ouvert (F13 §5.3).
        // Un profil embarqué corrompu interrompt l'installation (jamais de repli silencieux sur l'ouvert).
        if (!StartupProfileResolver.TryResolve(EmbeddedProfile.TryReadEmbeddedJson(), out IntegratorProfile profile, out string? profileError))
        {
            MessageBox.Show(
                profileError,
                "Liakont — profil intégrateur invalide",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var form = new WizardForm(engine, profile, KnownAdapters()))
        {
            Application.Run(form);
        }

        return 0;
    }

    private static int RunSilent(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage : Liakont.Agent.Installer.exe --silent <réponses.json> [--profile <profil.json>]");
            return 2;
        }

        // Précédence du profil en mode silencieux : --profile explicite (déploiement de masse choisi par
        // l'opérateur) > profil EMBARQUÉ (OPS08c) > profil par défaut ouvert. Un --profile explicite ou un
        // profil embarqué invalide interrompt — jamais de repli silencieux.
        string? explicitProfilePath = GetOption(args, "--profile");
        IntegratorProfile profile;
        string? profileError;
        if (explicitProfilePath != null)
        {
            if (!TryLoadProfile(explicitProfilePath, out profile, out profileError))
            {
                Console.Error.WriteLine(profileError);
                return 1;
            }
        }
        else if (!StartupProfileResolver.TryResolve(EmbeddedProfile.TryReadEmbeddedJson(), out profile, out profileError))
        {
            Console.Error.WriteLine(profileError);
            return 1;
        }

        InstallationInput input;
        try
        {
            input = AnswerFileLoader.Load(args[1]);
        }
        catch (AnswerFileFormatException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Lecture du fichier de réponses « {args[1]} » impossible : {ex.Message}");
            return 2;
        }

        var silent = new SilentInstaller(BuildEngine());
        return silent.Run(profile, input, Console.Out);
    }

    private static int RunUninstall(string[] args)
    {
        string? instanceName = GetOption(args, AgentInstance.CommandLineOption);
        if (instanceName == null && args.Length >= 2 && !args[1].StartsWith("--", StringComparison.Ordinal))
        {
            instanceName = args[1];
        }

        if (string.IsNullOrWhiteSpace(instanceName))
        {
            Console.Error.WriteLine("Usage : Liakont.Agent.Installer.exe --uninstall <instance>");
            return 2;
        }

        DeploymentOutcome outcome = BuildEngine().Uninstall(instanceName!);
        foreach (string line in outcome.ReportLines)
        {
            Console.Out.WriteLine(line);
        }

        return outcome.Success ? 0 : 1;
    }

    private static int RunListInstances()
    {
        IReadOnlyList<string> instances;
        try
        {
            instances = BuildEngine().ListInstalledInstances();
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (instances.Count == 0)
        {
            Console.Out.WriteLine("Aucune instance de l'agent n'est installée sur ce poste.");
            return 0;
        }

        Console.Out.WriteLine("Instances installées :");
        foreach (string name in instances)
        {
            Console.Out.WriteLine("  - " + name);
        }

        return 0;
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
        PrintProfilePlan(profile, Console.Out);
        return 0;
    }

    // Affiche le profil intégrateur EMBARQUÉ dans cet exécutable (OPS08c) — diagnostic de packaging. Un
    // profil embarqué corrompu échoue (anti-faux-vert) ; aucun profil embarqué = profil par défaut ouvert.
    private static int RunShowProfile()
    {
        string? embeddedJson = EmbeddedProfile.TryReadEmbeddedJson();
        if (embeddedJson == null)
        {
            Console.Out.WriteLine(
                "Aucun profil intégrateur n'est embarqué dans cet installeur : tous les champs sont " +
                "affichés et éditables (profil par défaut ouvert, F13 §5.3).");
            return 0;
        }

        if (!StartupProfileResolver.TryResolve(embeddedJson, out IntegratorProfile profile, out string? error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        Console.Out.WriteLine($"Profil intégrateur embarqué : « {profile.ProfileName} ».");
        if (!string.IsNullOrWhiteSpace(profile.Branding.Name))
        {
            Console.Out.WriteLine($"Marque : {profile.Branding.Name}");
        }

        Console.Out.WriteLine("Plan des champs :");
        PrintProfilePlan(profile, Console.Out);
        return 0;
    }

    private static void PrintProfilePlan(IntegratorProfile profile, TextWriter writer)
    {
        var engine = new IntegratorProfileEngine(profile);
        foreach (ResolvedField field in engine.ResolveAll())
        {
            string state = DescribeState(field.State);
            string editable = field.IsEditable ? "éditable" : "non éditable";
            string value = field.DefaultValue == null ? "(saisie au wizard)" : $"« {field.DefaultValue} »";
            writer.WriteLine($"  - {field.Key} : {state}, {editable}, défaut {value}");
        }
    }

    // Composition root des ports de production : sondes AGT05 (réutilisées), catalogue de services Windows,
    // déployeur de processus, chiffrement DPAPI (AGT01). Aucune logique métier (F13 §1).
    private static InstallerEngine BuildEngine()
    {
        ISecretProtector protector = new DpapiSecretProtector();
        var sourceProbe = new OdbcSourceConnectionProbe();
        var platformProbe = new HttpPlatformConnectionProbe();
        var catalog = new ServiceControllerInstanceCatalog();
        var deployer = new AgentProcessDeployer(protector, KnownAdapters());
        return new InstallerEngine(sourceProbe, platformProbe, catalog, deployer, protector);
    }

    // Source de vérité partagée avec le CLI via EmbeddedSourceAdapters — aucun nom inventé, aucune duplication.
    private static string[] KnownAdapters() => EmbeddedSourceAdapters.Names();

    // Charge un profil depuis un CHEMIN explicite (--profile en mode silencieux). Le profil embarqué et le
    // profil par défaut ouvert sont résolus par StartupProfileResolver (OPS08c) — pas ici.
    private static bool TryLoadProfile(string path, out IntegratorProfile profile, out string? error)
    {
        error = null;

        try
        {
            profile = IntegratorProfileLoader.Load(path);
        }
        catch (ProfileFormatException ex)
        {
            profile = StartupProfileResolver.DefaultOpenProfile();
            error = ex.Message;
            return false;
        }
        catch (IOException ex)
        {
            profile = StartupProfileResolver.DefaultOpenProfile();
            error = $"Lecture du profil « {path} » impossible : {ex.Message}";
            return false;
        }

        ProfileValidationResult validation = ProfileValidator.Validate(profile);
        if (!validation.IsValid)
        {
            error = $"Profil « {profile.ProfileName} » invalide :" + Environment.NewLine +
                string.Join(Environment.NewLine, validation.Errors.Select(e => "  - " + e));
            return false;
        }

        return true;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    // N'attache la console du parent que si stdout n'est PAS déjà redirigé : préserve une redirection
    // explicite du parent (capture stdout/stderr) tout en rendant les messages français visibles quand un
    // mode CLI est lancé nu depuis un terminal (un exe GUI n'a alors aucun handle stdout).
    private static void EnsureConsoleForCli()
    {
        IntPtr standardOutput = GetStdHandle(StdOutputHandle);
        if (standardOutput == IntPtr.Zero || standardOutput == new IntPtr(-1))
        {
            AttachConsole(AttachParentProcess);
        }
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
