namespace Liakont.Agent.Installer;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Liakont.Agent.Adapters.EncheresV6;
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
///   <item><c>--validate &lt;profil.json&gt;</c> : valide un profil et affiche son plan de champs (OPS08a, réutilisé par OPS08c).</item>
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
            default:
                Console.Error.WriteLine(
                    "Usage : Liakont.Agent.Installer.exe [sans argument = wizard] | " +
                    "--silent <réponses.json> [--profile <profil.json>] | --uninstall <instance> | " +
                    "--list-instances | --validate <profil.json>");
                return 2;
        }
    }

    private static int RunWizard()
    {
        InstallerEngine engine = BuildEngine();
        IntegratorProfile profile = OpenProfile();

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

        if (!TryLoadProfile(GetOption(args, "--profile"), out IntegratorProfile profile, out string? profileError))
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
        IReadOnlyList<string> instances = BuildEngine().ListInstalledInstances();
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

    // Liste des adaptateurs source EMBARQUÉS dans cette version (même source de vérité que le CLI AGT05 :
    // les plug-ins IExtractor réellement livrés) — aucun nom d'adaptateur inventé.
    private static string[] KnownAdapters() => new[] { new EncheresV6Extractor().SourceName };

    private static IntegratorProfile OpenProfile() =>
        new IntegratorProfile(
            "(profil par défaut)",
            IntegratorBranding.Empty,
            new Dictionary<string, FieldDeclaration>(StringComparer.Ordinal));

    private static bool TryLoadProfile(string? path, out IntegratorProfile profile, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            profile = OpenProfile();
            return true;
        }

        try
        {
            profile = IntegratorProfileLoader.Load(path!);
        }
        catch (ProfileFormatException ex)
        {
            profile = OpenProfile();
            error = ex.Message;
            return false;
        }
        catch (IOException ex)
        {
            profile = OpenProfile();
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
