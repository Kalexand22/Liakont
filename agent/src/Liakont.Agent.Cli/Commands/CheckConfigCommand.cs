namespace Liakont.Agent.Cli.Commands;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Core.Security;

/// <summary>
/// Commande <c>check-config</c> (F12 §2.1) : valide <c>agent.json</c> point par point — fichier
/// présent et bien formé, secrets (clé API, chaîne ODBC) déchiffrables, adaptateur connu. Sortie
/// lisible un point par ligne. Renvoie <see cref="CliExitCode.ProblemDetected"/> si un point échoue.
/// </summary>
internal sealed class CheckConfigCommand : ICliCommand
{
    private readonly string _defaultConfigPath;
    private readonly ISecretProtector _protector;
    private readonly IReadOnlyCollection<string> _knownAdapters;

    public CheckConfigCommand(string defaultConfigPath, ISecretProtector protector, IReadOnlyCollection<string> knownAdapters)
    {
        _defaultConfigPath = defaultConfigPath ?? throw new ArgumentNullException(nameof(defaultConfigPath));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _knownAdapters = knownAdapters ?? throw new ArgumentNullException(nameof(knownAdapters));
    }

    public string Name => "check-config";

    public string Description => "Valide agent.json (champs, secrets déchiffrables, adaptateur connu).";

    public int Execute(IReadOnlyList<string> args, TextWriter output)
    {
        string configPath = args.Count > 0 ? args[0] : _defaultConfigPath;
        output.WriteLine($"Vérification de la configuration : {configPath}");

        AgentConfig config;
        try
        {
            config = AgentConfigLoader.Load(configPath);
        }
        catch (AgentConfigException ex)
        {
            foreach (string error in ex.Errors)
            {
                output.WriteLine(CliFormat.Fail(error));
            }

            return CliExitCode.ProblemDetected;
        }

        output.WriteLine(CliFormat.Ok("Fichier agent.json présent et bien formé."));

        bool allOk = true;

        allOk &= ReportSecret(output, "Clé API", config.ApiKeyProtected, required: true);

        if (config.Extraction.OdbcConnectionStringProtected is null)
        {
            output.WriteLine(CliFormat.Ok($"Aucune chaîne ODBC (l'adaptateur « {config.Extraction.Adapter} » n'en utilise pas)."));
        }
        else
        {
            allOk &= ReportSecret(output, "Chaîne de connexion ODBC", config.Extraction.OdbcConnectionStringProtected, required: true);
        }

        allOk &= ReportAdapter(output, config.Extraction.Adapter);

        return allOk ? CliExitCode.Ok : CliExitCode.ProblemDetected;
    }

    private bool ReportSecret(TextWriter output, string label, string protectedValue, bool required)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            if (required)
            {
                output.WriteLine(CliFormat.Fail($"{label} absente."));
                return false;
            }

            return true;
        }

        try
        {
            _protector.Unprotect(protectedValue);
            output.WriteLine(CliFormat.Ok($"{label} déchiffrable (DPAPI)."));
            return true;
        }
        catch (Exception ex)
        {
            // Secret illisible : valeur non chiffrée par « encrypt », ou chiffrée sur une AUTRE machine
            // (DPAPI portée machine — voir SecretProtector). On NE journalise jamais la valeur (CLAUDE.md n°10).
            output.WriteLine(CliFormat.Fail($"{label} non déchiffrable : {ex.Message} Re-chiffrez-la sur CE poste avec « liakont-agent-cli encrypt »."));
            return false;
        }
    }

    private bool ReportAdapter(TextWriter output, string adapter)
    {
        if (_knownAdapters.Contains(adapter, StringComparer.OrdinalIgnoreCase))
        {
            output.WriteLine(CliFormat.Ok($"Adaptateur « {adapter} » reconnu."));
            return true;
        }

        string known = _knownAdapters.Count == 0 ? "(aucun)" : string.Join(", ", _knownAdapters.OrderBy(a => a, StringComparer.Ordinal));
        output.WriteLine(CliFormat.Fail($"Adaptateur « {adapter} » inconnu. Adaptateurs disponibles : {known}."));
        return false;
    }
}
