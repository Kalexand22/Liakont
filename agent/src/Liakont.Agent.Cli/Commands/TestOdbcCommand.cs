namespace Liakont.Agent.Cli.Commands;

using System;
using System.Collections.Generic;
using System.IO;
using Liakont.Agent.Cli.Diagnostics;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Core.Security;

/// <summary>
/// Commande <c>test-odbc</c> (F12 §2.1) : éprouve la connexion à la base source en LECTURE SEULE
/// (CLAUDE.md n°5) avec la chaîne ODBC déchiffrée, et affiche les tables détectées et leur nombre.
/// La sonde réelle est injectée (testable avec une doublure).
/// </summary>
internal sealed class TestOdbcCommand : ICliCommand
{
    private readonly string _defaultConfigPath;
    private readonly ISecretProtector _protector;
    private readonly Func<string, OdbcProbeResult> _probe;

    public TestOdbcCommand(string defaultConfigPath, ISecretProtector protector, Func<string, OdbcProbeResult> probe)
    {
        _defaultConfigPath = defaultConfigPath ?? throw new ArgumentNullException(nameof(defaultConfigPath));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public string Name => "test-odbc";

    public string Description => "Teste la connexion ODBC à la base source (lecture seule) et liste les tables.";

    public int Execute(IReadOnlyList<string> args, TextWriter output)
    {
        string configPath = args.Count > 0 ? args[0] : _defaultConfigPath;

        AgentConfig config;
        try
        {
            config = AgentConfigLoader.Load(configPath);
        }
        catch (AgentConfigException ex)
        {
            output.WriteLine(CliFormat.Fail("Configuration illisible : " + ex.Message));
            return CliExitCode.ProblemDetected;
        }

        if (config.Extraction.OdbcConnectionStringProtected is null)
        {
            output.WriteLine(CliFormat.Fail($"Aucune chaîne ODBC configurée pour l'adaptateur « {config.Extraction.Adapter} » — rien à tester."));
            return CliExitCode.ProblemDetected;
        }

        string connectionString;
        try
        {
            connectionString = _protector.Unprotect(config.Extraction.OdbcConnectionStringProtected);
        }
        catch (Exception ex)
        {
            output.WriteLine(CliFormat.Fail($"Chaîne ODBC non déchiffrable : {ex.Message} Re-chiffrez-la sur ce poste avec « liakont-agent-cli encrypt »."));
            return CliExitCode.ProblemDetected;
        }

        OdbcProbeResult result = _probe(connectionString);
        if (!result.Success)
        {
            output.WriteLine(CliFormat.Fail(result.Message ?? "Connexion ODBC impossible."));
            return CliExitCode.ProblemDetected;
        }

        output.WriteLine(CliFormat.Ok($"Connexion ODBC réussie — {result.Tables.Count} table(s) détectée(s)."));
        foreach (string table in result.Tables)
        {
            output.WriteLine($"  - {table}");
        }

        return CliExitCode.Ok;
    }
}
